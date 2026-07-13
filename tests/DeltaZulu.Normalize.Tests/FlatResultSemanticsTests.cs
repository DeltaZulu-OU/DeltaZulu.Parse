using Microsoft.VisualStudio.TestTools.UnitTesting;
using static DeltaZulu.Normalize.Tests.TestHelpers;

namespace DeltaZulu.Normalize.Tests;

/// <summary>
/// Pins the fixJSON special-name semantics the flat-result commit path must
/// reproduce exactly: the "." non-object fallback, merge-mode repeat ending
/// as a JSON null, and eager custom-type extraction under failOnDuplicate.
/// </summary>
[TestClass]
public class FlatResultSemanticsTests
{
    [TestMethod]
    public void DotName_NonObjectValue_IsStoredUnderLiteralDotKey()
    {
        /* "." only splices when the value is an object; a plain parser named
         * "." stores its string under the literal key "." */
        var (r, j) = TestHelpers.Normalize("rule=:a %.:word%", "a xyz");
        Assert.AreEqual(0, r);
        AssertJsonEquals("""{ ".": "xyz" }""", j);
    }

    [TestMethod]
    public void MergeRepeat_MismatchOnFirstRound_YieldsNullUnderDot()
    {
        /* merge-mode repeat with permitMismatchInParser failing on round 1
         * produces a JSON null committed under "." */
        const string rb = """
            rule=:l %{"name":".", "type":"repeat", "option.permitMismatchInParser":true,
                "parser": {"name":"n", "type":"number"},
                "while": {"type":"literal", "text":" "} }%X
            """;
        var (r, j) = TestHelpers.Normalize(rb, "l X");
        Assert.AreEqual(0, r);
        AssertJsonEquals("""{ ".": null }""", j);
    }

    [TestMethod]
    public void Repeat_FailOnDuplicate_CustomTypeSpliceAndFallback()
    {
        /* a custom type named "." inside a failOnDuplicate repeat is extracted
         * eagerly (its duplicate checks read the in-progress result) and its
         * fields are spliced into the merged result; when the type does not
         * match, the alternative branch runs instead */
        const string rb = """
            type=@pair:%a:char-to:/%/%b:word%
            rule=:l %{"name":".", "type":"repeat", "option.failOnDuplicate":true,
                "parser": {"type":"alternative", "parser":[ {"name":".", "type":"@pair"}, {"name":"c", "type":"word"} ]},
                "while": {"type":"literal", "text":" "} }%
            """;
        var (r, j) = TestHelpers.Normalize(rb, "l x/y z");
        Assert.AreEqual(0, r);
        AssertJsonEquals("""{ "a": "x", "b": "y", "c": "z" }""", j);
    }
}
