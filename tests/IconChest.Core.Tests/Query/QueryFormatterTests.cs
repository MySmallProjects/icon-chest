using IconChest.Core.Query;
using Xunit;

namespace IconChest.Core.Tests.Query;

public class QueryFormatterTests
{
    [Fact]
    public void Format_AllQueryExamples_RoundTripSuccessfully()
    {
        foreach (var example in QueryExamples.All)
        {
            var (ast1, err1) = QueryParser.Parse(example.Text);
            Assert.Null(err1);
            Assert.NotNull(ast1);

            string formatted = QueryFormatter.Format(ast1);
            Assert.False(string.IsNullOrWhiteSpace(formatted));

            var (ast2, err2) = QueryParser.Parse(formatted);
            Assert.Null(err2);
            Assert.NotNull(ast2);

            string reFormatted = QueryFormatter.Format(ast2);
            Assert.Equal(formatted, reFormatted);
        }
    }

    [Fact]
    public void Format_MessyInput_ProducesCanonicalNormalizedOutput()
    {
        var messy = "   (  TYPE   =  PNG   ||   tag  IN  (  'dog'  ,  'cat' )  )   &&   RATING  >=  4  ";
        var (ast, err) = QueryParser.Parse(messy);

        Assert.Null(err);
        Assert.NotNull(ast);

        string formatted = QueryFormatter.Format(ast);
        Assert.Equal("(type == png or tag in (\"dog\", \"cat\")) and rating >= 4", formatted);
    }
}

