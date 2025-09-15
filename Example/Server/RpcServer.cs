using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using BitRPC.Serialization;

namespace BitRPC.Server
{
    public abstract class BaseService
    {
        private readonly Dictionary<string, Func<object, object>> _methods;

        protected BaseService()
        {
            _methods = new Dictionary<string, Func<object, object>>();
            RegisterMethods();
        }

        protected abstract void RegisterMethods();

        protected void RegisterMethod<TRequest, TResponse>(string methodName, Func<TRequest, TResponse> method)
        {
            _methods[methodName] = request => method((TRequest)request);
        }

        protected void RegisterMethod<TRequest, TResponse>(string methodName, Func<TRequest, Task<TResponse>> method)
        {
            _methods[methodName] = request => method((TRequest)request).GetAwaiter().GetResult();
        }

        public object CallMethod(string methodName, object request)
        {
            if (_methods.TryGetValue(methodName, out var method))
            {
                return method(request);
            }
            throw new InvalidOperationException($"Method '{methodName}' not found");
        }

        public bool HasMethod(string methodName)
        {
            return _methods.ContainsKey(methodName);
        }
    }

    public class RpcServer
    {
        private readonly int _port;
        private readonly Dictionary<string, BaseService> _services;
        private System.Net.Sockets.TcpListener _listener;
        private bool _isRunning;
        private readonly List<System.Threading.Tasks.Task> _clientTasks;

        public RpcServer(int port)
        {
            _port = port;
            _services = new Dictionary<string, BaseService>();
            _clientTasks = new List<System.Threading.Tasks.Task>();
        }

        public void RegisterService(string name, BaseService service)
        {
            _services[name] = service;
        }

        public void Start()
        {
            _listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Any, _port);
            _listener.Start();
            _isRunning = true;

            System.Threading.Tasks.Task.Run(async () =>
            {
                while (_isRunning)
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    var clientTask = HandleClientAsync(client);
                    _clientTasks.Add(clientTask);
                }
            });
        }

        public void Stop()
        {
            _isRunning = false;
            _listener?.Stop();
            
            System.Threading.Tasks.Task.WhenAll(_clientTasks);
        }

        private async System.Threading.Tasks.Task HandleClientAsync(System.Net.Sockets.TcpClient client)
        {
            try
            {
                using (var stream = client.GetStream())
                {
                    while (client.Connected && _isRunning)
                    {
                        var lengthBytes = new byte[4];
                        await stream.ReadAsync(lengthBytes, 0, 4);
                        var length = BitConverter.ToInt32(lengthBytes, 0);
                        
                        var data = new byte[length];
                        await stream.ReadAsync(data, 0, length);
                        
                        var response = ProcessRequest(data);
                        
                        var responseBytes = response;
                        var responseLengthBytes = BitConverter.GetBytes(responseBytes.Length);
                        await stream.WriteAsync(responseLengthBytes, 0, responseLengthBytes.Length);
                        await stream.WriteAsync(responseBytes, 0, responseBytes.Length);
                        await stream.FlushAsync();
                    }
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                client.Close();
            }
        }

        private byte[] ProcessRequest(byte[] data)
        {
            try
            {
                var reader = new BitRPC.Serialization.StreamReader(data);
                var methodName = reader.ReadString();
                var serviceName = ExtractServiceName(methodName);
                var operationName = ExtractOperationName(methodName);

                if (_services.TryGetValue(serviceName, out var service))
                {
                    var request = reader.ReadObject();
                    var response = service.CallMethod(operationName, request);
                    
                    var writer = new BitRPC.Serialization.StreamWriter();
                    writer.WriteObject(response);
                    return writer.ToArray();
                }

                throw new InvalidOperationException($"Service '{serviceName}' not found");
            }
            catch (Exception ex)
            {
                var writer = new BitRPC.Serialization.StreamWriter();
                writer.WriteObject(ex);
                return writer.ToArray();
            }
        }

        private string ExtractServiceName(string methodName)
        {
            var dotIndex = methodName.IndexOf('.');
            return dotIndex > 0 ? methodName.Substring(0, dotIndex) : methodName;
        }

        private string ExtractOperationName(string methodName)
        {
            var dotIndex = methodName.IndexOf('.');
            return dotIndex > 0 ? methodName.Substring(dotIndex + 1) : methodName;
        }
    }
}