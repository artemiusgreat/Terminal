using Core.Models;
using Core.Services;
using Microsoft.Extensions.Configuration;
using Orleans.Hosting;
using Orleans.Providers;
using Orleans.TestingHost;

namespace Core.Tests
{
  public class SiloConfigurator : ISiloConfigurator, IClientBuilderConfigurator
  {
    public void Configure(ISiloBuilder orleans)
    {
      orleans.AddMemoryGrainStorageAsDefault();
      orleans.AddMemoryGrainStorage("PubSubStore");
      orleans.AddMemoryStreams<DefaultMemoryMessageBodySerializer>(nameof(Message));
    }

    public void Configure(IConfiguration configuration, IClientBuilder orleans)
    {
      orleans.AddMemoryStreams<DefaultMemoryMessageBodySerializer>(nameof(Message));
    }
  }
}
