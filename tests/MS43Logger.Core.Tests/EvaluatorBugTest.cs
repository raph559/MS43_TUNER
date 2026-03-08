using System;
using System.Collections.Generic;
using MS43Logger.Core;
using Xunit;

namespace MS43Logger.Core.Tests;

public class EvaluatorBugTest
{
    [Fact]
    public void TestBitwiseAnd()
    {
        var vars = new Dictionary<string, double> { { "X", 8192 + 1000 } };
        Assert.Equal(1000, ExpressionEvaluator.Evaluate("X&8191", vars));
    }
}
