using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Internal;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Primitives;

namespace FacebonkClickIDRemoverProxy
{
	public class CruftRemoverProxyMiddleware
	{
		ICruftRemoverConfiguration _configuration;
		IStatusConsoleSender _statusConsoleSender;
		RequestDelegate _next;

		public CruftRemoverProxyMiddleware(ICruftRemoverConfiguration configuration, ObjectPoolProvider objectPoolProvider, IStatusConsoleSender statusConsoleSender, RequestDelegate next)
		{
			_configuration = configuration;
			_statusConsoleSender = statusConsoleSender;
			_next = next;

			_client = new HttpClient();
			_bufferPool = objectPoolProvider.Create<ReadBuffer>();

			foreach (var property in typeof(HttpMethod).GetProperties(BindingFlags.Public | BindingFlags.Static))
				if ((property.PropertyType == typeof(HttpMethod)) && (property.GetIndexParameters().Length == 0))
					_httpMethodByName[property.Name] = (HttpMethod)property.GetValue(null);
		}

		HttpClient _client;
		ObjectPool<ReadBuffer> _bufferPool;

		class ReadBuffer
		{
			public readonly byte[] Buffer = new byte[65536];
		}

		class ReadBufferLease : IDisposable
		{
			ObjectPool<ReadBuffer> _pool;
			ReadBuffer _buffer;

			public ReadBuffer BufferObject => _buffer;

			public ReadBufferLease(ObjectPool<ReadBuffer> pool)
			{
				_pool = pool;
				_buffer = _pool.Get();
			}

			public void Dispose()
			{
				if (_pool != null)
				{
					_pool.Return(_buffer);
					_pool = null;
				}
			}
		}

		ConcurrentDictionary<string, HttpMethod> _httpMethodByName = new ConcurrentDictionary<string, HttpMethod>(StringComparer.InvariantCultureIgnoreCase);

		static int s_nextRequestID;

		public async Task InvokeAsync(HttpContext context)
		{
			int requestID = Interlocked.Increment(ref s_nextRequestID);

			_statusConsoleSender.NotifyNewRequest(requestID, context.Connection.RemoteIpAddress.ToString(), context.Request.Method, context.Request.Path);

			try
			{
				var forwardedRequestMessage = new HttpRequestMessage();

				if (!_httpMethodByName.TryGetValue(context.Request.Method, out var method))
				{
					method = new HttpMethod(context.Request.Method);

					_httpMethodByName.TryAdd(context.Request.Method, method);
				}

				foreach (var header in context.Request.Headers)
					forwardedRequestMessage.Headers.TryAddWithoutValidation(header.Key, (IEnumerable<string>)header.Value);

				var query = new Dictionary<string, StringValues>(context.Request.Query);

				query.Remove("fbclid");

				string queryString = "";

				if (query.Count > 0)
					queryString = QueryString.Create(query).ToString();

				forwardedRequestMessage.Method = method;
				forwardedRequestMessage.RequestUri = new Uri(_configuration.TargetBaseURI + context.Request.Path + queryString);

				if (!string.IsNullOrWhiteSpace(context.Request.ContentType))
				{
					forwardedRequestMessage.Content = new StreamContent(context.Request.Body);
					forwardedRequestMessage.Content.Headers.ContentType = new MediaTypeHeaderValue(context.Request.ContentType);
				}

				_statusConsoleSender.NotifyRequestSent(requestID);

				var response = await _client.SendAsync(forwardedRequestMessage, HttpCompletionOption.ResponseHeadersRead);

				context.Response.StatusCode = (int)response.StatusCode;

				foreach (var header in response.Headers.Concat(response.Content.Headers))
				{
					string firstValue = null;
					List<string> allValues = null;

					foreach (var value in header.Value)
					{
						if (firstValue == null)
							firstValue = value;
						else
						{
							if (allValues == null)
							{
								allValues = new List<string>();
								allValues.Add(firstValue);
							}

							allValues.Add(value);
						}
					}

					var values = allValues == null
						? new StringValues(firstValue)
						: new StringValues(allValues.ToArray());

					context.Response.Headers.Add(header.Key, values);

					if (header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
						_statusConsoleSender.NotifyRequestLength(requestID, long.Parse(firstValue));
				}

				using (var responseStream = await response.Content.ReadAsStreamAsync())
				using (var bufferLease = new ReadBufferLease(_bufferPool))
				{
					var buffer = bufferLease.BufferObject.Buffer;

					if (response.Content.Headers.ContentLength is long bytesRemaining)
					{
						while (bytesRemaining > 0)
						{
							int readLength = (bytesRemaining > buffer.Length) ? buffer.Length : (int)bytesRemaining;

							int bytesRead = await responseStream.ReadAsync(buffer, 0, readLength, context.RequestAborted);

							if (bytesRead <= 0)
								throw new Exception("Unexpected short read");

							await context.Response.Body.WriteAsync(buffer, 0, bytesRead);

							if (context.RequestAborted.IsCancellationRequested)
								break;

							bytesRemaining -= bytesRead;

							_statusConsoleSender.NotifyRequestProgress(requestID, bytesRead);
						}
					}
					else
					{
						while (true)
						{
							int bytesRead = await responseStream.ReadAsync(buffer, 0, buffer.Length, context.RequestAborted);

							if (bytesRead < 0)
								throw new Exception("I/O error");
							if (bytesRead == 0)
								break;

							await context.Response.Body.WriteAsync(buffer, 0, bytesRead);

							if (context.RequestAborted.IsCancellationRequested)
								break;

							_statusConsoleSender.NotifyRequestProgress(requestID, bytesRead);
						}
					}
				}
			}
			catch (TaskCanceledException) { }
			finally
			{
				_statusConsoleSender.NotifyRequestEnd(requestID);
			}
		}
	}
}
