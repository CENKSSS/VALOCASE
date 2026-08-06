using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.UI;

namespace ValoCase.EditorTests
{
    /// <summary>
    /// The mandatory update wall.
    ///
    /// <para>Two opposite failures are being guarded against, and both are severe. If the
    /// wall can be dismissed the feature does nothing — an outdated client keeps talking to
    /// a backend that has moved on. If the wall appears when it should not, every player is
    /// locked out of the game at once, with no button that lets them back in. The second is
    /// worse, which is why most of these tests are about <em>not</em> showing it.</para>
    ///
    /// <para>The off switch is an empty server value, and the malformed-input guard is what
    /// stops a typo in configuration from becoming a total outage. Neither may regress.</para>
    /// </summary>
    public class MandatoryUpdateTests
    {
        // ── The wall has no way out ───────────────────────────────────────────

        [Test]
        public void TheWallOffersExactlyOneButtonAndItIsUpdate()
        {
            var canvasGo = new GameObject("TestCanvas", typeof(Canvas));
            canvasGo.GetComponent<Canvas>().sortingOrder = 999;
            try
            {
                UpdateAvailablePopup.ResetForTests();
                UpdateAvailablePopup.TryShow(Bump(Application.version));

                var root = GameObject.Find("UpdateAvailablePopup");
                Assert.IsNotNull(root, "the wall was built");

                var buttons = root.GetComponentsInChildren<Button>(true);
                Assert.AreEqual(1, buttons.Length,
                    "exactly one control: a second button is a dismiss path and undoes the feature");

                var label = buttons[0].GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
                Assert.AreEqual("UPDATE", label.text);

                var labels = root.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true)
                                 .Select(t => t.text.ToUpperInvariant());
                foreach (var forbidden in new[] { "LATER", "SKIP", "CANCEL", "CLOSE", "DISMISS", "NOT NOW" })
                    CollectionAssert.DoesNotContain(labels, forbidden, forbidden + " would be a way out");
            }
            finally
            {
                CleanUp(canvasGo);
            }
        }

        [Test]
        public void TheOverlayCoversTheScreenAndSwallowsClicks()
        {
            var canvasGo = new GameObject("TestCanvas", typeof(Canvas));
            canvasGo.GetComponent<Canvas>().sortingOrder = 999;
            try
            {
                UpdateAvailablePopup.ResetForTests();
                UpdateAvailablePopup.TryShow(Bump(Application.version));

                var root = GameObject.Find("UpdateAvailablePopup");
                var rt = (RectTransform)root.transform;
                Assert.AreEqual(Vector2.zero, rt.anchorMin, "stretches from one corner");
                Assert.AreEqual(Vector2.one,  rt.anchorMax, "to the other");

                var img = root.GetComponent<Image>();
                Assert.IsTrue(img.raycastTarget,
                    "the backdrop must eat clicks, or the game underneath stays playable");
            }
            finally
            {
                CleanUp(canvasGo);
            }
        }

        [Test]
        public void TheBodyTellsThePlayerTheyMustUpdate()
        {
            var canvasGo = new GameObject("TestCanvas", typeof(Canvas));
            canvasGo.GetComponent<Canvas>().sortingOrder = 999;
            try
            {
                UpdateAvailablePopup.ResetForTests();
                UpdateAvailablePopup.TryShow("9.9.9");

                var root = GameObject.Find("UpdateAvailablePopup");
                var all = string.Join(" ", root.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true)
                                               .Select(t => t.text));
                StringAssert.Contains("9.9.9", all, "names the version to move to");
                StringAssert.Contains(Application.version, all, "and the one they are on");
                StringAssert.Contains("update", all.ToLowerInvariant());
            }
            finally
            {
                CleanUp(canvasGo);
            }
        }

        // ── The off switch, and everything that must not lock players out ─────

        [Test]
        public void AnEmptyServerValueShowsNothing()
        {
            // This is how the wall is turned off for every player at once. If it ever stops
            // working, a blank configuration becomes a global outage.
            foreach (var nothing in new[] { null, "", "   ", "\t" })
                Assert.IsFalse(UpdateAvailablePopup.IsNewer(nothing, "1.0.0"), "[" + (nothing ?? "null") + "]");
        }

        [Test]
        public void AMalformedServerValueLocksNobodyOut()
        {
            // Nothing here has a leading digit in its first segment, so none of it can be
            // read as a version at all and none of it puts a wall in front of anyone.
            foreach (var junk in new[] { "latest", "v1.0.2", "one.two.three", "..", "-1", "" })
                Assert.IsFalse(UpdateAvailablePopup.IsNewer(junk, "1.0.0"), junk);
        }

        [Test]
        public void ASuffixedVersionIsReadByItsLeadingDigits()
        {
            // Deliberate, not an oversight: a store build tagged "1.0.15b" or "1.0.2-beta"
            // is still that version and still has to be comparable. The parser reads the
            // leading digits of each segment and ignores the rest.
            //
            // The consequence is worth stating plainly, because it is the sharp edge of a
            // mandatory wall: "1.0.2-beta" walls everyone below 1.0.2. If a beta tag is
            // ever used as a store version, it gates real players just like a plain number.
            Assert.IsTrue(UpdateAvailablePopup.IsNewer("1.0.2-beta", "1.0.0"));
            Assert.IsTrue(UpdateAvailablePopup.IsNewer("1.0.15b", "1.0.5"));
            Assert.IsFalse(UpdateAvailablePopup.IsNewer("1.0.2-beta", "1.0.2"),
                "the suffix does not make it newer than the same number");
        }

        [Test]
        public void TheSameVersionIsNotNewer()
        {
            Assert.IsFalse(UpdateAvailablePopup.IsNewer("1.0.22", "1.0.22"));
            Assert.IsFalse(UpdateAvailablePopup.IsNewer("1.0", "1.0.0"), "1.0 == 1.0.0");
        }

        [Test]
        public void AnOlderServerValueDoesNotNagAPlayerWhoIsAhead()
        {
            // Exactly the state this project was in: the property said 1.0.20 while the
            // store served 1.0.22. A wall here would have blocked everyone for nothing.
            Assert.IsFalse(UpdateAvailablePopup.IsNewer("1.0.20", "1.0.22"));
        }

        // ── The comparison itself ─────────────────────────────────────────────

        [Test]
        public void ComparisonIsNumericPerSegmentNotAlphabetical()
        {
            // A string compare says "1.0.5" > "1.0.15", which would silently stop asking
            // anyone on 1.0.5 to update once the store reached double digits.
            Assert.IsTrue(UpdateAvailablePopup.IsNewer("1.0.15", "1.0.5"));
            Assert.IsTrue(UpdateAvailablePopup.IsNewer("1.0.10", "1.0.9"));
            Assert.IsTrue(UpdateAvailablePopup.IsNewer("1.1.0", "1.0.99"));
            Assert.IsTrue(UpdateAvailablePopup.IsNewer("2.0.0", "1.99.99"));
        }

        [Test]
        public void EveryLiveStoreBuildIsAskedToUpdateWhenTheServerMovesAhead()
        {
            // The versions actually in players' hands when this was written.
            foreach (var installed in new[] { "1.0.15", "1.0.19", "1.0.21" })
                Assert.IsTrue(UpdateAvailablePopup.IsNewer("1.0.22", installed), installed);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>One patch level above the running build, whatever it happens to be.</summary>
        static string Bump(string version)
        {
            var parts = version.Split('.');
            if (int.TryParse(parts[parts.Length - 1], out var last))
            {
                parts[parts.Length - 1] = (last + 1).ToString();
                return string.Join(".", parts);
            }
            return "99.99.99";
        }

        static void CleanUp(GameObject canvasGo)
        {
            var root = GameObject.Find("UpdateAvailablePopup");
            if (root != null) Object.DestroyImmediate(root);
            Object.DestroyImmediate(canvasGo);
            UpdateAvailablePopup.ResetForTests();
        }
    }
}
