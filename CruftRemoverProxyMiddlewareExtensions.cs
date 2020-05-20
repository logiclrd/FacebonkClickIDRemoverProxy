using Microsoft.AspNetCore.Builder;

namespace FacebonkClickIDRemoverProxy
{
	public static class CruftRemoverProxyMiddlewareExtensions
	{
		public static IApplicationBuilder UseCruftRemoverProxy(this IApplicationBuilder builder)
		{
			return builder.UseMiddleware<CruftRemoverProxyMiddleware>();
		}
	}
}
