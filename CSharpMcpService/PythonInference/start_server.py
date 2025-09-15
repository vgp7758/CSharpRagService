"""
启动 EmbeddingGemma 推理服务器的脚本
"""

import subprocess
import sys
import time
import requests
import os
from pathlib import Path

def check_dependencies():
    """检查依赖是否安装"""
    required_packages = [
        'torch', 'transformers', 'fastapi', 'uvicorn', 'pydantic', 'numpy'
    ]

    missing_packages = []
    for package in required_packages:
        try:
            __import__(package)
        except ImportError:
            missing_packages.append(package)

    if missing_packages:
        print(f"缺少以下依赖包: {', '.join(missing_packages)}")
        print("请运行: pip install -r requirements.txt")
        return False

    return True

def install_dependencies():
    """安装依赖"""
    print("正在安装依赖包...")
    try:
        subprocess.run([sys.executable, "-m", "pip", "install", "-r", "requirements.txt"],
                      check=True, capture_output=True, text=True)
        print("依赖安装完成")
        return True
    except subprocess.CalledProcessError as e:
        print(f"依赖安装失败: {e}")
        return False

def wait_for_server(url, timeout=300):
    """等待服务器启动"""
    print(f"等待服务器启动... {url}")
    start_time = time.time()

    while time.time() - start_time < timeout:
        try:
            response = requests.get(f"{url}/health", timeout=5)
            if response.status_code == 200:
                print("服务器启动成功！")
                return True
        except requests.RequestException:
            pass

        print(".", end="", flush=True)
        time.sleep(2)

    print(f"\n服务器启动超时 ({timeout}秒)")
    return False

def start_server():
    """启动推理服务器"""
    # 检查依赖
    if not check_dependencies():
        response = input("是否自动安装缺失的依赖? (y/n): ")
        if response.lower() == 'y':
            if not install_dependencies():
                return False
        else:
            return False

    # 启动服务器
    print("启动 EmbeddingGemma 推理服务器...")

    # 设置环境变量
    env = os.environ.copy()
    env['PYTHONPATH'] = str(Path(__file__).parent)

    try:
        # 启动服务器进程
        process = subprocess.Popen([
            sys.executable, "embedding_server.py"
        ], env=env, cwd=Path(__file__).parent)

        # 等待服务器启动
        if wait_for_server("http://localhost:8000"):
            print("\n服务器已就绪，可以在 C# MCP 服务中使用")
            print("按 Ctrl+C 停止服务器")

            try:
                # 等待用户中断
                process.wait()
            except KeyboardInterrupt:
                print("\n正在停止服务器...")
                process.terminate()
                process.wait()
                print("服务器已停止")
        else:
            print("服务器启动失败")
            process.terminate()
            return False

    except Exception as e:
        print(f"启动服务器时发生错误: {e}")
        return False

    return True

if __name__ == "__main__":
    start_server()