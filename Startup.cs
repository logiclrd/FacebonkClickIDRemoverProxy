using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace FacebonkClickIDRemoverProxy
{
	public class Startup
	{
		public void ConfigureServices(IServiceCollection services)
		{
			services.AddSingleton<ICruftRemoverConfiguration, CruftRemoverConfiguration>();
			services.AddSingleton<IStatusConsoleSender>(StatusConsole.Launch());
		}

		public void Configure(IApplicationBuilder app)
		{
			app.UseCruftRemoverProxy();
			app.UseDeveloperExceptionPage();
		}
	}
}
