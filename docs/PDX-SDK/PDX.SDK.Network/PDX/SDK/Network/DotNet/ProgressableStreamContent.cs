using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using PDX.SDK.Contracts.Util;

namespace PDX.SDK.Network.DotNet;

internal class ProgressableStreamContent : HttpContent
{
	private Stream content;

	private int bufferSize;

	private bool contentConsumed;

	private IChunkHandler chunkHandler;

	public ProgressableStreamContent(IChunkHandler uploadChunkHandler = null)
	{
		if (uploadChunkHandler == null || uploadChunkHandler.Stream == null)
		{
			throw new ArgumentNullException("Invalid parameter passed.");
		}
		content = uploadChunkHandler.Stream;
		bufferSize = uploadChunkHandler.BufferSize;
		chunkHandler = uploadChunkHandler;
	}

	protected override Task SerializeToStreamAsync(Stream stream, TransportContext context)
	{
		PrepareContent();
		return Task.Run(delegate
		{
			byte[] array = new byte[bufferSize];
			chunkHandler.Initialize();
			using (content)
			{
				int num = 1;
				while (num > 0)
				{
					num = content.Read(array, 0, array.Length);
					if (num > 0)
					{
						stream.Write(array, 0, num);
						chunkHandler.HandleBytes(null, num);
					}
				}
			}
			chunkHandler.Done();
		});
	}

	protected override bool TryComputeLength(out long length)
	{
		length = content.Length;
		return true;
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			content.Dispose();
		}
		((HttpContent)this).Dispose(disposing);
	}

	private void PrepareContent()
	{
		if (contentConsumed)
		{
			if (!content.CanSeek)
			{
				throw new InvalidOperationException("Net HTTP content stream already read");
			}
			content.Position = 0L;
		}
		contentConsumed = true;
	}
}
