namespace Looppaper.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void CoreAssemblyLoads()
    {
        Assert.Equal("Looppaper.Core", typeof(AssemblyMarker).Assembly.GetName().Name);
    }
}
