#!/usr/bin/env python3
"""
简单的MCP协议测试脚本
用于验证C# MCP服务是否正常工作
"""

import subprocess
import json
import sys
import os

def test_mcp_protocol():
    """测试MCP协议基本功能"""

    # 获取可执行文件路径
    exe_path = os.path.join(
        os.path.dirname(__file__),
        "CSharpMcpService",
        "bin",
        "Debug",
        "net8.0",
        "CSharpMcpService.exe"
    )

    if not os.path.exists(exe_path):
        print(f"错误: 找不到可执行文件 {exe_path}")
        print("请先运行: dotnet build")
        return False

    print(f"启动 MCP 服务: {exe_path}")

    try:
        # 启动MCP服务
        proc = subprocess.Popen(
            [exe_path],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            bufsize=1,
            universal_newlines=True
        )

        # 等待服务启动
        import time
        time.sleep(2)

        # 测试tools/list命令
        test_request = {
            "jsonrpc": "2.0",
            "id": 1,
            "method": "tools/list",
            "params": {}
        }

        print("发送 tools/list 请求...")
        proc.stdin.write(json.dumps(test_request) + "\n")
        proc.stdin.flush()

        # 读取响应
        response = proc.stdout.readline()
        if response:
            try:
                response_data = json.loads(response)
                print("✓ 收到响应:")
                print(f"  - ID: {response_data.get('id')}")
                print(f"  - 工具数量: {len(response_data.get('result', {}).get('tools', []))}")

                # 显示工具列表
                for tool in response_data.get('result', {}).get('tools', []):
                    print(f"    - {tool.get('name')}: {tool.get('description')}")

            except json.JSONDecodeError as e:
                print(f"✗ 响应解析错误: {e}")
                print(f"  原始响应: {response}")
        else:
            print("✗ 没有收到响应")

        # 测试不存在的工具
        test_request2 = {
            "jsonrpc": "2.0",
            "id": 2,
            "method": "tools/call",
            "params": {
                "name": "nonexistent_tool",
                "arguments": {}
            }
        }

        print("\n测试错误处理...")
        proc.stdin.write(json.dumps(test_request2) + "\n")
        proc.stdin.flush()

        response = proc.stdout.readline()
        if response:
            try:
                response_data = json.loads(response)
                if response_data.get('error'):
                    print("✓ 错误处理正常工作")
                    print(f"  - 错误代码: {response_data['error'].get('code')}")
                    print(f"  - 错误消息: {response_data['error'].get('message')}")
                else:
                    print("✗ 预期的错误响应未收到")
            except json.JSONDecodeError as e:
                print(f"✗ 响应解析错误: {e}")

        # 结束进程
        proc.terminate()
        proc.wait()

        print("\n✓ MCP 协议测试完成")
        return True

    except Exception as e:
        print(f"✗ 测试失败: {e}")
        return False

if __name__ == "__main__":
    print("=== C# MCP 服务协议测试 ===\n")
    success = test_mcp_protocol()
    sys.exit(0 if success else 1)