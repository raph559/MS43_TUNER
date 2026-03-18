using System.Globalization;

namespace MS43Logger.Core;

public static class ExpressionEvaluator
{
    public static double Evaluate(string expression, IReadOnlyDictionary<string, double> variables)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        ArgumentNullException.ThrowIfNull(variables);

        var parser = new Parser(expression, variables);
        return parser.Parse();
    }

    private sealed class Parser
    {
        private readonly string _expression;
        private readonly IReadOnlyDictionary<string, double> _variables;
        private int _position;

        public Parser(string expression, IReadOnlyDictionary<string, double> variables)
        {
            _expression = expression;
            _variables = variables;
        }

        public double Parse()
        {
            var value = ParseBitwiseAnd();
            SkipWhitespace();
            if (_position != _expression.Length)
            {
                throw new InvalidOperationException($"Unexpected token '{_expression[_position]}' in expression '{_expression}'.");
            }

            return value;
        }

        private double ParseBitwiseAnd()
        {
            var value = ParseExpression();
            while (true)
            {
                SkipWhitespace();
                if (!Match('&'))
                {
                    return value;
                }

                value = (long)value & (long)ParseExpression();
            }
        }

        private double ParseExpression()
        {
            var value = ParseTerm();
            while (true)
            {
                SkipWhitespace();
                if (Match('+'))
                {
                    value += ParseTerm();
                    continue;
                }

                if (Match('-'))
                {
                    value -= ParseTerm();
                    continue;
                }

                return value;
            }
        }

        private double ParseTerm()
        {
            var value = ParseFactor();
            while (true)
            {
                SkipWhitespace();
                if (Match('*'))
                {
                    value *= ParseFactor();
                    continue;
                }

                if (Match('/'))
                {
                    value /= ParseFactor();
                    continue;
                }

                return value;
            }
        }

        private double ParseFactor()
        {
            SkipWhitespace();

            if (Match('+'))
            {
                return ParseFactor();
            }

            if (Match('-'))
            {
                return -ParseFactor();
            }

            if (Match('('))
            {
                var value = ParseBitwiseAnd();
                Expect(')');
                return value;
            }

            var current = Peek();
            if (current is null)
            {
                throw new InvalidOperationException($"Unexpected end of expression '{_expression}'.");
            }

            if (char.IsDigit(current.Value) || current == '.')
            {
                return ParseNumber();
            }

            if (char.IsLetter(current.Value) || current == '_')
            {
                return ParseIdentifier();
            }

            throw new InvalidOperationException($"Unexpected token '{current}' in expression '{_expression}'.");
        }

        private double ParseNumber()
        {
            var start = _position;
            var seenDecimalPoint = false;

            while (_position < _expression.Length)
            {
                var current = _expression[_position];
                if (char.IsDigit(current))
                {
                    _position++;
                    continue;
                }

                if (!seenDecimalPoint && current == '.')
                {
                    seenDecimalPoint = true;
                    _position++;
                    continue;
                }

                break;
            }

            return double.Parse(_expression.AsSpan(start, _position - start), CultureInfo.InvariantCulture);
        }

        private double ParseIdentifier()
        {
            var start = _position;
            while (_position < _expression.Length)
            {
                var current = _expression[_position];
                if (char.IsLetterOrDigit(current) || current == '_')
                {
                    _position++;
                    continue;
                }

                break;
            }

            var identifier = _expression[start.._position];
            if (!_variables.TryGetValue(identifier, out var value))
            {
                throw new InvalidOperationException($"Unknown variable '{identifier}' in expression '{_expression}'.");
            }

            return value;
        }

        private void SkipWhitespace()
        {
            while (_position < _expression.Length && char.IsWhiteSpace(_expression[_position]))
            {
                _position++;
            }
        }

        private bool Match(char token)
        {
            if (Peek() == token)
            {
                _position++;
                return true;
            }

            return false;
        }

        private void Expect(char token)
        {
            SkipWhitespace();
            if (!Match(token))
            {
                throw new InvalidOperationException($"Expected '{token}' in expression '{_expression}'.");
            }
        }

        private char? Peek()
        {
            SkipWhitespace();
            return _position < _expression.Length ? _expression[_position] : null;
        }
    }
}
