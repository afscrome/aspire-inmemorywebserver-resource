namespace Aspire.Hosting.InMemoryWebServer.Tests;

public class BasicTests
{
    [Test]
    public async Task Add_ReturnsSum()
    {
        var result = 1 + 1;
        await Assert.That(result).IsEqualTo(2);
    }
}
