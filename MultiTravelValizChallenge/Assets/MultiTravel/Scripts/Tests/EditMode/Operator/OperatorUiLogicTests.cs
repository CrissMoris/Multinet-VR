using MultiTravel.Core.Session;
using MultiTravel.Operator;
using MultiTravel.Operator.UI;
using NUnit.Framework;

namespace MultiTravel.Tests.EditMode.Operator
{
    public sealed class OperatorUiLogicTests
    {
        [TestCase("", "")]
        [TestCase("0", "0")]
        [TestCase("05321234567", "0532 123 45 67")]
        [TestCase("0532 123 45 67", "0532 123 45 67")]
        [TestCase("0532-123-45-67", "0532 123 45 67")]
        [TestCase("0532 1", "0532 1")]
        [TestCase("0532 ", "0532")]
        [TestCase("5321234567", "532 123 45 67")]
        [TestCase("+905321234567", "+90 532 123 45 67")]
        [TestCase("+", "+")]
        [TestCase("abc", "")]
        public void PhoneFormatter_FormatsAsTyped(string raw, string expected)
        {
            Assert.AreEqual(expected, PhoneFormatter.Format(raw));
        }

        [Test]
        public void PhoneFormatter_IsIdempotent()
        {
            const string raw = "05321234567";
            string once = PhoneFormatter.Format(raw);
            Assert.AreEqual(once, PhoneFormatter.Format(once));
        }

        [Test]
        public void PhoneFormatter_KeepsExtraDigitsAndCapsAtFifteen()
        {
            string formatted = PhoneFormatter.Format("0532123456789012345678");
            int digits = 0;
            foreach (char c in formatted)
            {
                if (char.IsDigit(c))
                {
                    digits++;
                }
            }

            Assert.AreEqual(PhoneFormatter.MaxDigits, digits);
            Assert.IsFalse(formatted.EndsWith(" "));
        }

        [TestCase(SessionState.Welcome, 0)]
        [TestCase(SessionState.Registration, 1)]
        [TestCase(SessionState.GenderSelection, 2)]
        [TestCase(SessionState.Instructions, 3)]
        [TestCase(SessionState.Loading, 4)]
        [TestCase(SessionState.Countdown, 4)]
        [TestCase(SessionState.Playing, 4)]
        [TestCase(SessionState.Completed, 5)]
        [TestCase(SessionState.Submitting, 5)]
        [TestCase(SessionState.SubmissionFailed, 5)]
        [TestCase(SessionState.Finished, 5)]
        [TestCase(SessionState.Fatal, -1)]
        public void Stepper_MapsEverySessionState(SessionState state, int expected)
        {
            Assert.AreEqual(expected, Stepper.StepFor(state));
        }

        [Test]
        public void UiTween_EaseOutCubic_IsMonotonicAndClamped()
        {
            Assert.AreEqual(0f, UiTween.EaseOutCubic(-1f), 1e-6f);
            Assert.AreEqual(1f, UiTween.EaseOutCubic(2f), 1e-6f);
            float previous = 0f;
            for (int i = 1; i <= 20; i++)
            {
                float value = UiTween.EaseOutCubic(i / 20f);
                Assert.GreaterOrEqual(value, previous);
                previous = value;
            }

            Assert.Greater(UiTween.EaseOutCubic(0.5f), 0.5f, "ease-out should be ahead of linear at the midpoint");
        }

        [Test]
        public void ElapsedTextBuffer_PlainFormat_MatchesMinutesSecondsTenths()
        {
            var buffer = new ElapsedTextBuffer(false);
            int length = buffer.Format(83_450, out bool changed);
            Assert.IsTrue(changed);
            Assert.AreEqual("01:23.4", buffer.LastText(length));

            buffer.Format(83_460, out changed);
            Assert.IsFalse(changed, "same tenth must not rewrite the label");
        }

        [Test]
        public void ElapsedTextBuffer_Tabular_WrapsDigitGroupsInMspace()
        {
            var buffer = new ElapsedTextBuffer(true);
            int length = buffer.Format(5_000, out _);
            string text = buffer.LastText(length);
            StringAssert.Contains("<mspace=", text);
            Assert.AreEqual("00:05.0", StripTags(text));
        }

        private static string StripTags(string text)
        {
            var result = new System.Text.StringBuilder();
            bool inTag = false;
            foreach (char c in text)
            {
                if (c == '<')
                {
                    inTag = true;
                }
                else if (c == '>')
                {
                    inTag = false;
                }
                else if (!inTag)
                {
                    result.Append(c);
                }
            }

            return result.ToString();
        }
    }
}
