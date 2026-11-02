namespace IconChest.Core.Query;

public sealed class QueryParser
{
    public const int MaxNestingDepth = 20;
    public const int MaxTerms = 200;
    public const int MaxInListValues = 100;
    public const int MaxPatternLength = 200;

    private readonly IReadOnlyList<QueryToken> _tokens;
    private int _pos;
    private int _depth;
    private int _termCount;

    private QueryParser(IReadOnlyList<QueryToken> tokens)
    {
        _tokens = tokens;
        _pos = 0;
        _depth = 0;
        _termCount = 0;
    }

    public static (QueryNode? Ast, QueryError? Error) Parse(string text)
    {
        var (tokens, lexError) = QueryLexer.Tokenize(text);
        if (lexError != null)
        {
            return (null, lexError);
        }

        if (tokens == null || tokens.Count == 0 || tokens[0].Type == QueryTokenType.EndOfInput)
        {
            // Empty or blank query matches everything
            return (null, null);
        }

        var parser = new QueryParser(tokens);
        return parser.ParseRoot();
    }

    private (QueryNode? Ast, QueryError? Error) ParseRoot()
    {
        if (Match(QueryTokenType.EndOfInput))
        {
            return (null, null);
        }

        var (expr, error) = ParseOrExpr();
        if (error != null) return (null, error);

        if (!Match(QueryTokenType.EndOfInput))
        {
            var cur = Peek();
            if (cur.Type == QueryTokenType.RParen)
            {
                return (null, new QueryError(
                    QueryErrorCodes.UnbalancedParenthesis,
                    "Unmatched closing parenthesis ')'.",
                    cur.Position,
                    cur.Length));
            }

            if (CanStartExpression(cur.Type))
            {
                return (null, new QueryError(
                    QueryErrorCodes.MissingConnective,
                    $"Missing connective between terms. Did you mean to use 'and'?",
                    cur.Position,
                    cur.Length,
                    "and"));
            }

            return (null, new QueryError(
                QueryErrorCodes.UnexpectedToken,
                $"Unexpected token '{cur.Text}'.",
                cur.Position,
                cur.Length));
        }

        return (expr, null);
    }

    private (QueryNode? Node, QueryError? Error) ParseOrExpr()
    {
        var (left, err) = ParseAndExpr();
        if (err != null) return (null, err);

        while (Match(QueryTokenType.Or))
        {
            var opTok = Previous();
            if (Peek().Type == QueryTokenType.EndOfInput)
            {
                return (null, new QueryError(
                    QueryErrorCodes.UnexpectedEnd,
                    "Expected an expression after 'or'.",
                    opTok.Position + opTok.Length,
                    0));
            }
            if (Peek().Type == QueryTokenType.RParen)
            {
                var rparen = Peek();
                return (null, new QueryError(
                    QueryErrorCodes.ExpectedExpression,
                    "Expected an expression after connective, found ')'.",
                    rparen.Position,
                    rparen.Length));
            }

            var (right, rightErr) = ParseAndExpr();
            if (rightErr != null) return (null, rightErr);

            int start = left!.Position;
            int len = (right!.Position + right.Length) - start;
            left = new OrNode(left, right, start, len);
        }

        return (left, null);
    }

    private (QueryNode? Node, QueryError? Error) ParseAndExpr()
    {
        var (left, err) = ParseUnary();
        if (err != null) return (null, err);

        while (true)
        {
            if (Match(QueryTokenType.And))
            {
                var opTok = Previous();
                if (Peek().Type == QueryTokenType.EndOfInput)
                {
                    return (null, new QueryError(
                        QueryErrorCodes.UnexpectedEnd,
                        "Expected an expression after 'and'.",
                        opTok.Position + opTok.Length,
                        0));
                }
                if (Peek().Type == QueryTokenType.RParen)
                {
                    var rparen = Peek();
                    return (null, new QueryError(
                        QueryErrorCodes.ExpectedExpression,
                        "Expected an expression after connective, found ')'.",
                        rparen.Position,
                        rparen.Length));
                }

                var (right, rightErr) = ParseUnary();
                if (rightErr != null) return (null, rightErr);

                int start = left!.Position;
                int len = (right!.Position + right.Length) - start;
                left = new AndNode(left, right, start, len);
                continue;
            }

            // Check if adjacent term without connective (missing_connective error)
            var next = Peek();
            if (next.Type != QueryTokenType.Or &&
                next.Type != QueryTokenType.RParen &&
                next.Type != QueryTokenType.EndOfInput &&
                CanStartExpression(next.Type))
            {
                return (null, new QueryError(
                    QueryErrorCodes.MissingConnective,
                    $"Missing connective between terms. Did you mean to use 'and'?",
                    next.Position,
                    next.Length,
                    "and"));
            }

            break;
        }

        return (left, null);
    }

    private (QueryNode? Node, QueryError? Error) ParseUnary()
    {
        if (Match(QueryTokenType.Not))
        {
            var notTok = Previous();
            _depth++;
            if (_depth > MaxNestingDepth)
            {
                return (null, new QueryError(
                    QueryErrorCodes.NestingTooDeep,
                    $"Query nesting depth exceeds maximum limit of {MaxNestingDepth}.",
                    notTok.Position,
                    notTok.Length));
            }

            if (Peek().Type == QueryTokenType.EndOfInput)
            {
                return (null, new QueryError(
                    QueryErrorCodes.UnexpectedEnd,
                    "Expected an expression after 'not'.",
                    notTok.Position + notTok.Length,
                    0));
            }
            if (Peek().Type == QueryTokenType.RParen)
            {
                var rparen = Peek();
                return (null, new QueryError(
                    QueryErrorCodes.ExpectedExpression,
                    "Expected an expression after 'not', found ')'.",
                    rparen.Position,
                    rparen.Length));
            }

            var (operand, err) = ParseUnary();
            _depth--;
            if (err != null) return (null, err);

            int start = notTok.Position;
            int len = (operand!.Position + operand.Length) - start;
            return (new NotNode(operand, start, len), null);
        }

        return ParsePrimary();
    }

    private (QueryNode? Node, QueryError? Error) ParsePrimary()
    {
        if (Match(QueryTokenType.LParen))
        {
            var lparen = Previous();
            _depth++;
            if (_depth > MaxNestingDepth)
            {
                return (null, new QueryError(
                    QueryErrorCodes.NestingTooDeep,
                    $"Query nesting depth exceeds maximum limit of {MaxNestingDepth}.",
                    lparen.Position,
                    lparen.Length));
            }

            if (Match(QueryTokenType.RParen))
            {
                var rparen = Previous();
                int spanLen = (rparen.Position + rparen.Length) - lparen.Position;
                return (null, new QueryError(
                    QueryErrorCodes.EmptyGroup,
                    "Empty group '()' is not allowed.",
                    lparen.Position,
                    spanLen));
            }

            if (Peek().Type == QueryTokenType.EndOfInput)
            {
                return (null, new QueryError(
                    QueryErrorCodes.UnexpectedEnd,
                    "Expected an expression after '('.",
                    lparen.Position + lparen.Length,
                    0));
            }

            var (expr, err) = ParseOrExpr();
            if (err != null) return (null, err);

            if (!Match(QueryTokenType.RParen))
            {
                if (Peek().Type == QueryTokenType.EndOfInput)
                {
                    return (null, new QueryError(
                        QueryErrorCodes.UnbalancedParenthesis,
                        "Unclosed parenthesis '('.",
                        lparen.Position,
                        lparen.Length));
                }

                var offending = Peek();
                return (null, new QueryError(
                    QueryErrorCodes.UnbalancedParenthesis,
                    "Expected closing parenthesis ')'.",
                    offending.Position,
                    offending.Length));
            }

            _depth--;
            return (expr, null);
        }

        return ParsePredicate();
    }

    private (QueryNode? Node, QueryError? Error) ParsePredicate()
    {
        var cur = Peek();

        // 1. Quoted string is a bare search term
        if (cur.Type == QueryTokenType.String)
        {
            Advance();
            return CheckAndRegisterTerm(new BareTermNode(cur, cur.Position, cur.Length), cur);
        }

        // 2. Unquoted word
        if (cur.Type == QueryTokenType.Word)
        {
            var fieldTok = Advance();
            var next = Peek();

            // 2a. Word cmpOp Value
            if (next.Type == QueryTokenType.CmpOp)
            {
                var opTok = Advance();
                var valTok = Peek();
                if (valTok.Type is not (QueryTokenType.Word or QueryTokenType.String))
                {
                    if (valTok.Type == QueryTokenType.EndOfInput)
                    {
                        return (null, new QueryError(
                            QueryErrorCodes.ExpectedValue,
                            $"Expected value after '{opTok.Text}'.",
                            opTok.Position + opTok.Length,
                            0));
                    }

                    return (null, new QueryError(
                        QueryErrorCodes.ExpectedValue,
                        $"Expected value after '{opTok.Text}', found '{valTok.Text}'.",
                        valTok.Position,
                        valTok.Length));
                }

                Advance();
                int len = (valTok.Position + valTok.Length) - fieldTok.Position;
                return CheckAndRegisterTerm(
                    new ComparisonNode(fieldTok, opTok, valTok, fieldTok.Position, len),
                    fieldTok);
            }

            // 2b. Word [not] in (...)
            if (next.Type == QueryTokenType.In || (next.Type == QueryTokenType.Not && PeekAhead(1).Type == QueryTokenType.In))
            {
                bool isNot = false;
                if (next.Type == QueryTokenType.Not)
                {
                    isNot = true;
                    Advance(); // skip Not
                }
                Advance(); // skip In

                if (!Match(QueryTokenType.LParen))
                {
                    var offending = Peek();
                    return (null, new QueryError(
                        QueryErrorCodes.ExpectedExpression,
                        "Expected '(' after 'in'.",
                        offending.Position,
                        offending.Length));
                }

                var lparen = Previous();
                if (Match(QueryTokenType.RParen))
                {
                    var rparen = Previous();
                    int spanLen = (rparen.Position + rparen.Length) - lparen.Position;
                    return (null, new QueryError(
                        QueryErrorCodes.EmptyList,
                        "Empty list '()' is not allowed in 'in' predicate.",
                        lparen.Position,
                        spanLen));
                }

                var values = new List<QueryToken>();
                while (true)
                {
                    var vTok = Peek();
                    if (vTok.Type is not (QueryTokenType.Word or QueryTokenType.String))
                    {
                        return (null, new QueryError(
                            QueryErrorCodes.ExpectedValue,
                            "Expected a value in 'in' list.",
                            vTok.Position,
                            vTok.Length));
                    }

                    Advance();
                    values.Add(vTok);

                    if (values.Count > MaxInListValues)
                    {
                        return (null, new QueryError(
                            QueryErrorCodes.TooManyValues,
                            $"'in' list exceeds maximum allowed limit of {MaxInListValues} values.",
                            vTok.Position,
                            vTok.Length));
                    }

                    if (Match(QueryTokenType.Comma))
                    {
                        if (Peek().Type == QueryTokenType.EndOfInput)
                        {
                            return (null, new QueryError(
                                QueryErrorCodes.UnexpectedEnd,
                                "Expected value after ',' in 'in' list.",
                                Peek().Position,
                                0));
                        }
                        continue;
                    }

                    if (Match(QueryTokenType.RParen))
                    {
                        break;
                    }

                    if (Peek().Type == QueryTokenType.EndOfInput)
                    {
                        return (null, new QueryError(
                            QueryErrorCodes.UnexpectedEnd,
                            "Expected ')' or ',' to complete 'in' list.",
                            Peek().Position,
                            0));
                    }

                    var off = Peek();
                    return (null, new QueryError(
                        QueryErrorCodes.UnexpectedToken,
                        $"Expected ',' or ')' in 'in' list, found '{off.Text}'.",
                        off.Position,
                        off.Length));
                }

                var rParenTok = Previous();
                int span = (rParenTok.Position + rParenTok.Length) - fieldTok.Position;
                return CheckAndRegisterTerm(
                    new InNode(fieldTok, isNot, values, fieldTok.Position, span),
                    fieldTok);
            }

            // 2c. Word is [not] empty
            if (next.Type == QueryTokenType.Is)
            {
                Advance(); // skip Is
                bool isNot = false;
                if (Match(QueryTokenType.Not))
                {
                    isNot = true;
                }

                if (!Match(QueryTokenType.Empty))
                {
                    var off = Peek();
                    return (null, new QueryError(
                        QueryErrorCodes.ExpectedOperator,
                        $"Expected 'empty' after 'is{(isNot ? " not" : "")}', found '{off.Text}'.",
                        off.Position,
                        off.Length));
                }

                var emptyTok = Previous();
                int len = (emptyTok.Position + emptyTok.Length) - fieldTok.Position;
                return CheckAndRegisterTerm(
                    new IsEmptyNode(fieldTok, isNot, fieldTok.Position, len),
                    fieldTok);
            }

            // 2d. Word [not] like Value
            if (next.Type == QueryTokenType.Like || (next.Type == QueryTokenType.Not && PeekAhead(1).Type == QueryTokenType.Like))
            {
                bool isNot = false;
                if (next.Type == QueryTokenType.Not)
                {
                    isNot = true;
                    Advance(); // skip Not
                }
                Advance(); // skip Like

                var patTok = Peek();
                if (patTok.Type is not (QueryTokenType.String or QueryTokenType.Word))
                {
                    return (null, new QueryError(
                        QueryErrorCodes.ExpectedValue,
                        "Expected string pattern after 'like'.",
                        patTok.Position,
                        patTok.Length));
                }

                Advance();
                if (patTok.Text.Length > MaxPatternLength)
                {
                    return (null, new QueryError(
                        QueryErrorCodes.PatternTooLong,
                        $"Pattern length exceeds maximum limit of {MaxPatternLength} characters.",
                        patTok.Position,
                        patTok.Length));
                }

                int len = (patTok.Position + patTok.Length) - fieldTok.Position;
                return CheckAndRegisterTerm(
                    new LikeNode(fieldTok, isNot, patTok, fieldTok.Position, len),
                    fieldTok);
            }

            // 2e. Word [not] contains Value
            if (next.Type == QueryTokenType.Contains || (next.Type == QueryTokenType.Not && PeekAhead(1).Type == QueryTokenType.Contains))
            {
                bool isNot = false;
                if (next.Type == QueryTokenType.Not)
                {
                    isNot = true;
                    Advance(); // skip Not
                }
                Advance(); // skip Contains

                var valTok = Peek();
                if (valTok.Type is not (QueryTokenType.String or QueryTokenType.Word))
                {
                    return (null, new QueryError(
                        QueryErrorCodes.ExpectedValue,
                        "Expected value after 'contains'.",
                        valTok.Position,
                        valTok.Length));
                }

                Advance();
                int len = (valTok.Position + valTok.Length) - fieldTok.Position;
                return CheckAndRegisterTerm(
                    new ContainsNode(fieldTok, isNot, valTok, fieldTok.Position, len),
                    fieldTok);
            }

            // 2f. Word [not] under Value
            if (next.Type == QueryTokenType.Under || (next.Type == QueryTokenType.Not && PeekAhead(1).Type == QueryTokenType.Under))
            {
                bool isNot = false;
                if (next.Type == QueryTokenType.Not)
                {
                    isNot = true;
                    Advance(); // skip Not
                }
                Advance(); // skip Under

                var valTok = Peek();
                if (valTok.Type is not (QueryTokenType.String or QueryTokenType.Word))
                {
                    return (null, new QueryError(
                        QueryErrorCodes.ExpectedValue,
                        "Expected directory path string after 'under'.",
                        valTok.Position,
                        valTok.Length));
                }

                Advance();
                int len = (valTok.Position + valTok.Length) - fieldTok.Position;
                return CheckAndRegisterTerm(
                    new UnderNode(fieldTok, isNot, valTok, fieldTok.Position, len),
                    fieldTok);
            }

            // 2g. Lone word -> bare term or boolean shorthand (validator checks)
            return CheckAndRegisterTerm(
                new BareTermNode(fieldTok, fieldTok.Position, fieldTok.Length),
                fieldTok);
        }

        if (cur.Type == QueryTokenType.RParen)
        {
            return (null, new QueryError(
                QueryErrorCodes.UnbalancedParenthesis,
                "Unmatched closing parenthesis ')'.",
                cur.Position,
                cur.Length));
        }

        // 3. Other unexpected tokens
        return (null, new QueryError(
            QueryErrorCodes.UnexpectedToken,
            $"Unexpected token '{cur.Text}'.",
            cur.Position,
            cur.Length));
    }

    private (QueryNode? Node, QueryError? Error) CheckAndRegisterTerm(QueryNode node, QueryToken token)
    {
        _termCount++;
        if (_termCount > MaxTerms)
        {
            return (null, new QueryError(
                QueryErrorCodes.TooManyTerms,
                $"Query exceeds maximum limit of {MaxTerms} terms.",
                token.Position,
                token.Length));
        }

        return (node, null);
    }

    private static bool CanStartExpression(QueryTokenType type)
    {
        return type is QueryTokenType.Word or
                       QueryTokenType.String or
                       QueryTokenType.LParen or
                       QueryTokenType.Not;
    }

    private QueryToken Peek() => _pos < _tokens.Count ? _tokens[_pos] : _tokens[^1];

    private QueryToken PeekAhead(int offset)
    {
        int idx = _pos + offset;
        return idx < _tokens.Count ? _tokens[idx] : _tokens[^1];
    }

    private QueryToken Advance()
    {
        var cur = Peek();
        if (_pos < _tokens.Count) _pos++;
        return cur;
    }

    private QueryToken Previous() => _pos > 0 ? _tokens[_pos - 1] : _tokens[0];

    private bool Match(QueryTokenType type)
    {
        if (Peek().Type == type)
        {
            Advance();
            return true;
        }
        return false;
    }
}
