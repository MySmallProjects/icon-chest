using System.Diagnostics;
using System.Text;
using IconChest.Core.Query;
using Xunit;

namespace IconChest.Core.Tests.Query;

public class QueryFuzzTests
{
    [Fact]
    public void Fuzz_5000Cases_NeverThrowsUnhandledExceptionAndFinishesUnder100MsEach()
    {
        var rng = new Random(1337);
        var baseQueries = QueryExamples.All.Select(e => e.Text).ToArray();
        const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 =!<>()[],._*?\"'\\/:\r\n\t&|!@#$%^;+-~`";

        var sw = new Stopwatch();

        for (int i = 0; i < 5000; i++)
        {
            string candidate;

            if (rng.Next(2) == 0 && baseQueries.Length > 0)
            {
                // Mutate an existing valid query
                var baseQ = baseQueries[rng.Next(baseQueries.Length)];
                var sb = new StringBuilder(baseQ);
                int mutations = rng.Next(1, 5);

                for (int m = 0; m < mutations; m++)
                {
                    int op = rng.Next(3);
                    if (op == 0 && sb.Length > 0)
                    {
                        // Delete char
                        sb.Remove(rng.Next(sb.Length), 1);
                    }
                    else if (op == 1)
                    {
                        // Insert char
                        char c = alphabet[rng.Next(alphabet.Length)];
                        sb.Insert(rng.Next(sb.Length + 1), c);
                    }
                    else if (sb.Length > 1)
                    {
                        // Swap chars
                        int idx1 = rng.Next(sb.Length);
                        int idx2 = rng.Next(sb.Length);
                        (sb[idx1], sb[idx2]) = (sb[idx2], sb[idx1]);
                    }
                }
                candidate = sb.ToString();
            }
            else
            {
                // Generate completely random string
                int length = rng.Next(1, 150);
                var sb = new StringBuilder(length);
                for (int l = 0; l < length; l++)
                {
                    sb.Append(alphabet[rng.Next(alphabet.Length)]);
                }
                candidate = sb.ToString();
            }

            sw.Restart();
            var result = QueryEngine.ParseAndCompile(candidate);
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds < 100, $"Fuzz iteration {i} took too long: {sw.ElapsedMilliseconds} ms for input: '{candidate}'");
            // Must have produced either valid query or error list
            if (result.IsValid)
            {
                Assert.NotNull(result.Query);
            }
            else
            {
                Assert.NotEmpty(result.Errors);
            }
        }
    }
}

