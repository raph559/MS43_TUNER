using System.Text.RegularExpressions;
using MS43Logger.Core;

namespace MS43Logger.Core.Tests;

public class ExpressionEvaluatorTests
{
    [Theory]
    [InlineData("0.1*X+1.3877788e-017", 33, 3.3)]
    [InlineData("0.0014648439*X+1.5610694e-007", 1000, 1.4648440561)]
    [InlineData("2e3", 0, 2000)]
    [InlineData("2E+1*X", 2, 40)]
    public void ScientificNotationIsANumber(string equation, double x, double expected)
    {
        Assert.Equal(expected, ExpressionEvaluator.Evaluate(equation, new Dictionary<string, double> { ["X"] = x }), 9);
    }

    [Fact]
    public void ALetterEAfterADigitWithoutExponentIsStillAnError()
    {
        Assert.Throws<InvalidOperationException>(() => ExpressionEvaluator.Evaluate("2e", new Dictionary<string, double>()));
    }

    [Fact]
    public void BundledAdxExpressionsAreUnaffectedByExponentParsing()
    {
        // Exponent parsing only changes inputs where a digit is followed by e/E; no bundled ADX equation has one.
        var exponent = new Regex(@"(?<![A-Za-z_])\d+(\.\d*)?[eE]");
        foreach (var variant in BundledDefinitions.Variants)
        {
            foreach (var value in BundledDefinitions.Load(variant).Values)
            {
                Assert.DoesNotMatch(exponent, value.Equation);
            }
        }
    }
}
