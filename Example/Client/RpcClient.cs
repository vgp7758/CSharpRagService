using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using BitRPC.Serialization;

namespace BitRPC.Client
{
    public interface IRpcClient
    {
        Task<TResponse> CallAsync<TRequest, TResponse>(string method, TRequest request);
        Task ConnectAsync();
        Task DisconnectAsync();
        bool IsConnected { get; }
    }

    public abstract class BaseClient
    {
        protected readonly IRpcClient _client;

        protected BaseClient(IRpcClient client)
        {
            _client = client;
        }

        protected async Task<TResponse> CallAsync<TRequest, TResponse>(string method, TRequest request)
        {
            return await _client.CallAsync<TRequest, TResponse>(method, request);
        }

        public async Task ConnectAsync()
        {
            await _client.ConnectAsync();
        }

        public async Task DisconnectAsync()
        {
            await _client.DisconnectAsync();
        }

        public bool IsConnected => _client.IsConnected;
    }

    public class TcpRpcClient : IRpcClient
    {
        private readonly string _host;
        private readonly int _port;
        private TcpClient _tcpClient;
        private NetworkStream _stream;

        public TcpRpcClient(string host, int port)
        {
            _host = host;
            _port = port;
        }

        public bool IsConnected => _tcpClient?.Connected == true;

        public async Task ConnectAsync()
        {
            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(_host, _port);
            _stream = _tcpClient.GetStream();
        }

        public async Task DisconnectAsync()
        {
            if (_stream != null)
            {
                _stream.Close();
                _stream = null;
            }
            if (_tcpClient != null)
            {
                _tcpClient.Close();
                _tcpClient = null;
            }
        }

        public async Task<TResponse> CallAsync<TRequest, TResponse>(string method, TRequest request)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Client is not connected");
            }

            var serializer = BitRPC.Serialization.BufferSerializer.Instance;
            
            var writer = new BitRPC.Serialization.StreamWriter();
            writer.WriteString(method);
            writer.WriteObject(request);
            
            var data = writer.ToArray();
            var requestLengthBytes = BitConverter.GetBytes(data.Length);
            await _stream.WriteAsync(requestLengthBytes, 0, requestLengthBytes.Length);
            await _stream.WriteAsync(data, 0, data.Length);
            await _stream.FlushAsync();

            var responseLengthBytes = new byte[4];
            await _stream.ReadAsync(responseLengthBytes, 0, 4);
            var length = BitConverter.ToInt32(responseLengthBytes, 0);
            
            var responseData = new byte[length];
            await _stream.ReadAsync(responseData, 0, length);
            
            var reader = new BitRPC.Serialization.StreamReader(responseData);
            return (TResponse)reader.ReadObject();
        }
    }
}