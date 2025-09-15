using CSharpMcpService.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace CSharpMcpService.Services;

public class ProjectMonitoringService
{
    private readonly ILogger<ProjectMonitoringService> _logger;
    private readonly IncrementalVectorDatabase _vectorDatabase;
    private readonly IProjectAnalyzer _projectAnalyzer;
    private readonly IEmbeddingService _embeddingService;
    private readonly ConcurrentDictionary<string, ProjectMonitor> _monitoredProjects = new();
    private readonly Timer _monitoringTimer;
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    public ProjectMonitoringService(
        ILogger<ProjectMonitoringService> logger,
        IncrementalVectorDatabase vectorDatabase,
        IProjectAnalyzer projectAnalyzer,
        IEmbeddingService embeddingService)
    {
        _logger = logger;
        _vectorDatabase = vectorDatabase;
        _projectAnalyzer = projectAnalyzer;
        _embeddingService = embeddingService;

        // 每分钟检查一次
        _monitoringTimer = new Timer(MonitorProjectsAsync, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public async Task StartMonitoringAsync()
    {
        _logger.LogInformation("Starting project monitoring service");

        // 加载持久化的项目状态
        await LoadMonitoredProjectsAsync();

        _logger.LogInformation("Project monitoring service started. Checking for changes every minute.");
    }

    public async Task AddProjectAsync(string projectPath)
    {
        try
        {
            if (!File.Exists(projectPath))
            {
                throw new FileNotFoundException($"Project file not found: {projectPath}");
            }

            if (!projectPath.EndsWith(".csproj"))
            {
                throw new ArgumentException("File must be a .csproj file");
            }

            var projectInfo = await _projectAnalyzer.AnalyzeProjectAsync(projectPath);
            var projectDirectory = Path.GetDirectoryName(projectPath)!;

            var monitor = new ProjectMonitor
            {
                ProjectId = projectInfo.Id,
                ProjectPath = projectPath,
                ProjectDirectory = projectDirectory,
                ProjectName = projectInfo.Name,
                LastCheckTime = DateTime.UtcNow,
                LastUpdateTime = DateTime.UtcNow,
                IsActive = true,
                FileWatchers = new List<FileSystemWatcher>()
            };

            // 创建文件监控器
            CreateFileWatchers(monitor);

            _monitoredProjects[projectInfo.Id] = monitor;

            // 保存监控状态
            await SaveMonitoredProjectsAsync();

            _logger.LogInformation("Added project to monitoring: {ProjectName} ({ProjectId})", projectInfo.Name, projectInfo.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding project to monitoring: {ProjectPath}", projectPath);
            throw;
        }
    }

    public async Task RemoveProjectAsync(string projectId)
    {
        try
        {
            if (_monitoredProjects.TryRemove(projectId, out var monitor))
            {
                // 停止文件监控器
                foreach (var watcher in monitor.FileWatchers)
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }

                monitor.IsActive = false;

                // 保存监控状态
                await SaveMonitoredProjectsAsync();

                _logger.LogInformation("Removed project from monitoring: {ProjectName} ({ProjectId})", monitor.ProjectName, projectId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing project from monitoring: {ProjectId}", projectId);
            throw;
        }
    }

    private void CreateFileWatchers(ProjectMonitor monitor)
    {
        // 监控 .cs 文件
        var csWatcher = new FileSystemWatcher(monitor.ProjectDirectory)
        {
            Filter = "*.cs",
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        csWatcher.Changed += (sender, e) => OnFileChanged(monitor, e.FullPath, "Changed");
        csWatcher.Created += (sender, e) => OnFileChanged(monitor, e.FullPath, "Created");
        csWatcher.Deleted += (sender, e) => OnFileChanged(monitor, e.FullPath, "Deleted");
        csWatcher.Renamed += (sender, e) => OnFileChanged(monitor, e.FullPath, "Renamed");

        // 监控 .csproj 文件
        var csprojWatcher = new FileSystemWatcher(monitor.ProjectDirectory)
        {
            Filter = "*.csproj",
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };

        csprojWatcher.Changed += (sender, e) => OnFileChanged(monitor, e.FullPath, "ProjectChanged");
        csprojWatcher.Created += (sender, e) => OnFileChanged(monitor, e.FullPath, "ProjectChanged");

        monitor.FileWatchers.Add(csWatcher);
        monitor.FileWatchers.Add(csprojWatcher);

        _logger.LogDebug("Created file watchers for project: {ProjectName}", monitor.ProjectName);
    }

    private void OnFileChanged(ProjectMonitor monitor, string filePath, string changeType)
    {
        try
        {
            // 记录变化，但延迟处理以避免频繁更新
            monitor.PendingChanges[filePath] = (DateTime.UtcNow, changeType);
            _logger.LogDebug("File {ChangeType}: {FilePath} for project {ProjectName}", changeType, filePath, monitor.ProjectName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling file change event: {FilePath}", filePath);
        }
    }

    private async void MonitorProjectsAsync(object? state)
    {
        try
        {
            if (_cancellationTokenSource.Token.IsCancellationRequested)
                return;

            var projectsToCheck = _monitoredProjects.Values.Where(p => p.IsActive).ToList();

            foreach (var monitor in projectsToCheck)
            {
                await CheckProjectForUpdatesAsync(monitor);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in project monitoring timer");
        }
    }

    private async Task CheckProjectForUpdatesAsync(ProjectMonitor monitor)
    {
        try
        {
            // 如果有待处理的变化，立即处理
            if (monitor.PendingChanges.Count > 0)
            {
                await ProcessPendingChangesAsync(monitor);
                return;
            }

            // 定期检查文件修改时间
            var hasChanges = await CheckForFileModificationsAsync(monitor);

            if (hasChanges)
            {
                await TriggerIncrementalUpdateAsync(monitor, "Periodic check detected changes");
            }
            else
            {
                monitor.LastCheckTime = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking project for updates: {ProjectName}", monitor.ProjectName);
        }
    }

    private async Task<bool> CheckForFileModificationsAsync(ProjectMonitor monitor)
    {
        try
        {
            // 获取项目中的所有相关文件
            var projectFiles = new List<string>();

            // 获取所有 .cs 文件
            if (Directory.Exists(monitor.ProjectDirectory))
            {
                projectFiles.AddRange(Directory.GetFiles(monitor.ProjectDirectory, "*.cs", SearchOption.AllDirectories));
                projectFiles.AddRange(Directory.GetFiles(monitor.ProjectDirectory, "*.csproj", SearchOption.TopDirectoryOnly));
            }

            // 检查是否有文件被修改
            foreach (var filePath in projectFiles)
            {
                try
                {
                    var lastModified = File.GetLastWriteTimeUtc(filePath);

                    // 如果文件在最后检查时间之后被修改，标记为需要更新
                    if (lastModified > monitor.LastCheckTime)
                    {
                        _logger.LogDebug("File modified since last check: {FilePath}", filePath);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error checking file modification time: {FilePath}", filePath);
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for file modifications: {ProjectName}", monitor.ProjectName);
            return false;
        }
    }

    private async Task ProcessPendingChangesAsync(ProjectMonitor monitor)
    {
        try
        {
            // 防抖动：等待2秒确保文件写入完成
            await Task.Delay(2000);

            if (monitor.PendingChanges.Count > 0)
            {
                var changeCount = monitor.PendingChanges.Count;
                var recentChanges = monitor.PendingChanges.Where(kvp =>
                    kvp.Value.Time > DateTime.UtcNow.AddSeconds(-30)).ToList();

                if (recentChanges.Any())
                {
                    await TriggerIncrementalUpdateAsync(monitor, $"Processing {recentChanges.Count} recent file changes");
                }

                // 清理旧的变化记录
                var cutoffTime = DateTime.UtcNow.AddMinutes(-5);
                var oldChanges = monitor.PendingChanges.Where(kvp => kvp.Value.Time <= cutoffTime).ToList();
                foreach (var oldChange in oldChanges)
                {
                    monitor.PendingChanges.TryRemove(oldChange.Key, out _);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing pending changes: {ProjectName}", monitor.ProjectName);
        }
    }

    private async Task TriggerIncrementalUpdateAsync(ProjectMonitor monitor, string reason)
    {
        try
        {
            _logger.LogInformation("Triggering incremental update for {ProjectName}: {Reason}", monitor.ProjectName, reason);

            // 检查项目文件是否仍然存在
            if (!File.Exists(monitor.ProjectPath))
            {
                _logger.LogWarning("Project file no longer exists: {ProjectPath}", monitor.ProjectPath);
                await RemoveProjectAsync(monitor.ProjectId);
                return;
            }

            // 重新分析项目
            var projectInfo = await _projectAnalyzer.AnalyzeProjectAsync(monitor.ProjectPath);
            var newSymbols = await _projectAnalyzer.ExtractSymbolsAsync(projectInfo);

            // 执行增量更新
            var summary = await _vectorDatabase.UpdateProjectAsync(monitor.ProjectId, newSymbols, _embeddingService);

            monitor.LastUpdateTime = DateTime.UtcNow;
            monitor.LastCheckTime = DateTime.UtcNow;
            monitor.PendingChanges.Clear();

            // 保存状态
            await SaveMonitoredProjectsAsync();

            _logger.LogInformation("Incremental update completed for {ProjectName}: {Added} added, {Updated} updated, {Removed} removed",
                monitor.ProjectName, summary.AddedSymbols, summary.UpdatedSymbols, summary.RemovedSymbols);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error triggering incremental update: {ProjectName}", monitor.ProjectName);
        }
    }

    private async Task SaveMonitoredProjectsAsync()
    {
        try
        {
            var projectsToSave = _monitoredProjects.Values.Select(m => new ProjectMonitorData
            {
                ProjectId = m.ProjectId,
                ProjectPath = m.ProjectPath,
                ProjectDirectory = m.ProjectDirectory,
                ProjectName = m.ProjectName,
                LastCheckTime = m.LastCheckTime,
                LastUpdateTime = m.LastUpdateTime,
                IsActive = m.IsActive
            }).ToList();

            var data = new
            {
                Version = 1,
                LastSaved = DateTime.UtcNow,
                Projects = projectsToSave
            };

            var json = System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });

            var savePath = Path.Combine(Directory.GetCurrentDirectory(), "monitored_projects.json");
            await File.WriteAllTextAsync(savePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving monitored projects");
        }
    }

    private async Task LoadMonitoredProjectsAsync()
    {
        try
        {
            var loadPath = Path.Combine(Directory.GetCurrentDirectory(), "monitored_projects.json");

            if (!File.Exists(loadPath))
                return;

            var json = await File.ReadAllTextAsync(loadPath);
            var data = System.Text.Json.JsonSerializer.Deserialize<MonitoredProjectsData>(json);

            if (data?.Projects != null)
            {
                foreach (var projectData in data.Projects)
                {
                    try
                    {
                        if (File.Exists(projectData.ProjectPath) && projectData.IsActive)
                        {
                            var monitor = new ProjectMonitor
                            {
                                ProjectId = projectData.ProjectId,
                                ProjectPath = projectData.ProjectPath,
                                ProjectDirectory = projectData.ProjectDirectory,
                                ProjectName = projectData.ProjectName,
                                LastCheckTime = projectData.LastCheckTime,
                                LastUpdateTime = projectData.LastUpdateTime,
                                IsActive = projectData.IsActive,
                                FileWatchers = new List<FileSystemWatcher>()
                            };

                            CreateFileWatchers(monitor);
                            _monitoredProjects[projectData.ProjectId] = monitor;

                            _logger.LogInformation("Loaded monitored project: {ProjectName}", projectData.ProjectName);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error loading monitored project: {ProjectName}", projectData.ProjectName);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading monitored projects");
        }
    }

    public void Dispose()
    {
        try
        {
            _cancellationTokenSource.Cancel();
            _monitoringTimer?.Dispose();

            foreach (var monitor in _monitoredProjects.Values)
            {
                foreach (var watcher in monitor.FileWatchers)
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }
            }

            _cancellationTokenSource.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing project monitoring service");
        }
    }

    public Dictionary<string, ProjectMonitorStatus> GetMonitoringStatus()
    {
        return _monitoredProjects.Values.ToDictionary(
            m => m.ProjectId,
            m => new ProjectMonitorStatus
            {
                ProjectName = m.ProjectName,
                ProjectPath = m.ProjectPath,
                IsActive = m.IsActive,
                LastCheckTime = m.LastCheckTime,
                LastUpdateTime = m.LastUpdateTime,
                PendingChangesCount = m.PendingChanges.Count,
                FileWatchersCount = m.FileWatchers.Count(w => w.EnableRaisingEvents)
            }
        );
    }
}

// 监控项目信息
public class ProjectMonitor
{
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectPath { get; set; } = string.Empty;
    public string ProjectDirectory { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public DateTime LastCheckTime { get; set; }
    public DateTime LastUpdateTime { get; set; }
    public bool IsActive { get; set; }
    public List<FileSystemWatcher> FileWatchers { get; set; } = new();
    public ConcurrentDictionary<string, (DateTime Time, string ChangeType)> PendingChanges { get; set; } = new();
}

// 监控状态
public class ProjectMonitorStatus
{
    public string ProjectName { get; set; } = string.Empty;
    public string ProjectPath { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime LastCheckTime { get; set; }
    public DateTime LastUpdateTime { get; set; }
    public int PendingChangesCount { get; set; }
    public int FileWatchersCount { get; set; }
}

// 持久化数据模型
internal class MonitoredProjectsData
{
    public int Version { get; set; }
    public DateTime LastSaved { get; set; }
    public List<ProjectMonitorData> Projects { get; set; } = new();
}

internal class ProjectMonitorData
{
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectPath { get; set; } = string.Empty;
    public string ProjectDirectory { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public DateTime LastCheckTime { get; set; }
    public DateTime LastUpdateTime { get; set; }
    public bool IsActive { get; set; }
}