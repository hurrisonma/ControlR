using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using ControlR.Agent.Common.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ControlR.Agent.Common.Services;

public class StableStationAssistanceGateServer(
  IStableStationAssistanceGate gate,
  IOptions<StableStationAssistanceGateOptions> options,
  ILogger<StableStationAssistanceGateServer> logger) : BackgroundService
{
  private const int MaxConcurrentClients = 8;
  private const int MaxRequestBytes = 8192;
  private readonly SemaphoreSlim _clientSlots = new(MaxConcurrentClients, MaxConcurrentClients);
  private readonly IStableStationAssistanceGate _gate = gate;
  private readonly ILogger<StableStationAssistanceGateServer> _logger = logger;
  private readonly StableStationAssistanceGateOptions _options = options.Value;
  private TcpListener? _listener;
  private byte[] _sharedSecret = [];

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    if (!_options.Enabled)
    {
      return;
    }

    ValidateOptions();
    _sharedSecret = Convert.FromBase64String(
      (await File.ReadAllTextAsync(_options.SharedSecretFile, stoppingToken)).Trim());
    if (_sharedSecret.Length < 32)
    {
      throw new InvalidOperationException("StableStation assistance gate secret must contain at least 32 bytes.");
    }

    _listener = new TcpListener(IPAddress.Loopback, _options.Port);
    _listener.Start(8);
    _logger.LogInformation(
      "StableStation assistance gate listening on loopback port {Port} for endpoint {EndpointId}.",
      _options.Port,
      _options.EndpointId);

    try
    {
      while (!stoppingToken.IsCancellationRequested)
      {
        await _clientSlots.WaitAsync(stoppingToken);
        try
        {
          var client = await _listener.AcceptTcpClientAsync(stoppingToken);
          _ = HandleClient(client, stoppingToken);
        }
        catch
        {
          _clientSlots.Release();
          throw;
        }
      }
    }
    finally
    {
      _listener.Stop();
      CryptographicOperations.ZeroMemory(_sharedSecret);
    }
  }

  private static async Task WriteResponse(
    Stream stream,
    int statusCode,
    object payload,
    CancellationToken cancellationToken)
  {
    var body = JsonSerializer.SerializeToUtf8Bytes(payload);
    var reason = statusCode == 200 ? "OK" : statusCode == 404 ? "Not Found" : statusCode == 403 ? "Forbidden" : "Bad Request";
    var headers = Encoding.ASCII.GetBytes(
      $"HTTP/1.1 {statusCode} {reason}\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
    await stream.WriteAsync(headers, cancellationToken);
    await stream.WriteAsync(body, cancellationToken);
  }

  private async Task HandleClient(TcpClient client, CancellationToken cancellationToken)
  {
    try
    {
      using (client)
      {
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestCancellation.CancelAfter(TimeSpan.FromSeconds(3));
        var requestToken = requestCancellation.Token;
        try
        {
          client.ReceiveTimeout = 3000;
          client.SendTimeout = 3000;
          await using var stream = client.GetStream();
          using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
          var requestLine = await reader.ReadLineAsync(requestToken);
          if (requestLine != "POST /v1/gate HTTP/1.1")
          {
            await WriteResponse(stream, 404, new { ok = false, error = "not_found" }, requestToken);
            return;
          }

          var contentLength = -1;
          var headerBytes = requestLine.Length;
          var headersComplete = false;
          for (var i = 0; i < 32; i++)
          {
            var line = await reader.ReadLineAsync(requestToken);
            if (line is null)
            {
              return;
            }
            headerBytes += line.Length;
            if (headerBytes > MaxRequestBytes)
            {
              throw new InvalidDataException("Gate request headers are too large.");
            }
            if (line.Length == 0)
            {
              headersComplete = true;
              break;
            }
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
              int.TryParse(line[15..].Trim(), out contentLength);
            }
          }

          if (!headersComplete)
          {
            await WriteResponse(stream, 400, new { ok = false, error = "invalid_headers" }, requestToken);
            return;
          }

          if (contentLength is <= 0 or > MaxRequestBytes)
          {
            await WriteResponse(stream, 400, new { ok = false, error = "invalid_content_length" }, requestToken);
            return;
          }

          var body = new char[contentLength];
          var read = 0;
          while (read < contentLength)
          {
            var count = await reader.ReadAsync(body.AsMemory(read, contentLength - read), requestToken);
            if (count == 0)
            {
              throw new EndOfStreamException();
            }
            read += count;
          }

          var command = JsonSerializer.Deserialize<StableStationAssistanceGateCommand>(
            body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
          if (command is null)
          {
            await WriteResponse(stream, 400, new { ok = false, error = "invalid_command" }, requestToken);
            return;
          }
          if (!_gate.Apply(command, _sharedSecret, out var reason))
          {
            await WriteResponse(stream, 403, new { ok = false, error = reason }, requestToken);
            return;
          }
          await WriteResponse(stream, 200, new { ok = true }, requestToken);
        }
        catch (OperationCanceledException)
        {
          // Service shutdown and the per-request deadline both fail closed.
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
          _logger.LogWarning(ex, "Rejected malformed StableStation assistance gate request.");
        }
      }
    }
    finally
    {
      _clientSlots.Release();
    }
  }

  private void ValidateOptions()
  {
    if (_options.Port is < 1024 or > 65535 ||
        string.IsNullOrWhiteSpace(_options.EndpointId) ||
        string.IsNullOrWhiteSpace(_options.SharedSecretFile) ||
        !File.Exists(_options.SharedSecretFile))
    {
      throw new InvalidOperationException(
        "Enabled StableStation assistance gate requires EndpointId, Port, and an existing SharedSecretFile.");
    }
  }

}
