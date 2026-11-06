using System.IO;
using Fomoxa.Rapier.PlayerCheck;
using NUnit.Framework;

namespace Fomoxa.Networking.Rapier.Tests
{
    public sealed class GoldenScenarioTest
    {
        [Test]
        public void TheCSharpScenarioGivesTheGoldenHashesOfTheCrate()
        {
            string expected = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "golden.txt")).Replace("\r", "").TrimEnd('\n');
            Assert.That(string.Join("\n", GoldenScenario.Run()), Is.EqualTo(expected));
        }
    }
}
