using NUnit.Framework;

namespace RetrofitLocalization.Tests
{
    public class KoreanParticlesTests
    {
        // Nouns used below, with and without a final consonant:
        //   검 geom (sword)        ends in a consonant
        //   사과 sagwa (apple) ends in a vowel
        //   물 mul (water)         ends in the consonant rieul
        //   서울 Seoul         ends in rieul
        //   부산 Busan         ends in a consonant
        //   제주 Jeju          ends in a vowel

        [TestCase("\uAC80\uC740(\uB294)", "\uAC80\uC740")]                         // topic after a consonant: eun
        [TestCase("\uC0AC\uACFC\uC740(\uB294)", "\uC0AC\uACFC\uB294")]             // topic after a vowel: neun
        [TestCase("\uAC80\uC774(\uAC00)", "\uAC80\uC774")]                         // subject after a consonant: i
        [TestCase("\uC0AC\uACFC\uC774(\uAC00)", "\uC0AC\uACFC\uAC00")]             // subject after a vowel: ga
        [TestCase("\uAC80\uC744(\uB97C)", "\uAC80\uC744")]                         // object after a consonant: eul
        [TestCase("\uC0AC\uACFC\uC744(\uB97C)", "\uC0AC\uACFC\uB97C")]             // object after a vowel: reul
        [TestCase("\uAC80\uACFC(\uC640)", "\uAC80\uACFC")]                         // "and" after a consonant: gwa
        [TestCase("\uC0AC\uACFC\uACFC(\uC640)", "\uC0AC\uACFC\uC640")]             // "and" after a vowel: wa
        public void Particle_FollowsTheFinalConsonant(string written, string expected)
        {
            Assert.That(KoreanParticles.Resolve(written), Is.EqualTo(expected));
        }

        [TestCase("\uAC80\uB294(\uC740)", "\uAC80\uC740")]
        [TestCase("\uC0AC\uACFC\uAC00(\uC774)", "\uC0AC\uACFC\uAC00")]
        [TestCase("\uC0AC\uACFC\uB97C(\uC744)", "\uC0AC\uACFC\uB97C")]
        [TestCase("\uAC80\uC640(\uACFC)", "\uAC80\uACFC")]
        public void Particle_AcceptsEitherSpellingOfThePair(string written, string expected)
        {
            Assert.That(KoreanParticles.Resolve(written), Is.EqualTo(expected));
        }

        [TestCase("\uBD80\uC0B0\uC73C\uB85C(\uB85C)", "\uBD80\uC0B0\uC73C\uB85C")] // consonant: euro
        [TestCase("\uC81C\uC8FC\uC73C\uB85C(\uB85C)", "\uC81C\uC8FC\uB85C")]       // vowel: ro
        [TestCase("\uC11C\uC6B8\uC73C\uB85C(\uB85C)", "\uC11C\uC6B8\uB85C")]       // rieul: ro, not euro
        [TestCase("\uBB3C\uB85C(\uC73C\uB85C)", "\uBB3C\uB85C")]                   // rieul, other spelling
        [TestCase("\uC11C\uC6B8(\uC73C)\uB85C", "\uC11C\uC6B8\uB85C")]             // rieul, "(eu)ro" spelling
        [TestCase("\uBD80\uC0B0(\uC73C)\uB85C", "\uBD80\uC0B0\uC73C\uB85C")]
        [TestCase("\uC81C\uC8FC(\uC73C)\uB85C", "\uC81C\uC8FC\uB85C")]
        public void DirectionParticle_TreatsRieulLikeAVowel(string written, string expected)
        {
            Assert.That(KoreanParticles.Resolve(written), Is.EqualTo(expected));
        }

        [Test]
        public void RieulException_AppliesOnlyToTheDirectionParticle()
        {
            // After rieul every other particle still takes its consonant form.
            Assert.That(KoreanParticles.Resolve("\uBB3C\uC740(\uB294)"), Is.EqualTo("\uBB3C\uC740"));
            Assert.That(KoreanParticles.Resolve("\uBB3C\uC744(\uB97C)"), Is.EqualTo("\uBB3C\uC744"));
        }

        [Test]
        public void NonHangulBeforeTheParticle_KeepsBothForms()
        {
            Assert.That(KoreanParticles.Resolve("Tester\uC740(\uB294)"), Is.EqualTo("Tester\uC740(\uB294)"));
            Assert.That(KoreanParticles.Resolve("42\uC744(\uB97C)"), Is.EqualTo("42\uC744(\uB97C)"));
        }

        [Test]
        public void ParticleAtTheStart_IsLeftAlone()
        {
            Assert.That(KoreanParticles.Resolve("\uC740(\uB294)"), Is.EqualTo("\uC740(\uB294)"));
        }

        [Test]
        public void RichTextTagsAndClosingQuotes_AreSkipped()
        {
            Assert.That(KoreanParticles.Resolve("<b>\uAC80</b>\uC744(\uB97C)"), Is.EqualTo("<b>\uAC80</b>\uC744"));
            Assert.That(KoreanParticles.Resolve("'\uC0AC\uACFC'\uC744(\uB97C)"), Is.EqualTo("'\uC0AC\uACFC'\uB97C"));
            Assert.That(KoreanParticles.Resolve("<color=red>\uC0AC\uACFC</color>\uC774(\uAC00)"),
                Is.EqualTo("<color=red>\uC0AC\uACFC</color>\uAC00"));
        }

        [Test]
        public void SeveralParticlesInOneSentence_AreAllResolved()
        {
            // "<sword>eun(neun) <apple>eul(reul) ..." -> "<sword>eun <apple>reul ..."
            string written = "\uAC80\uC740(\uB294) \uC0AC\uACFC\uC744(\uB97C) \uC11C\uC6B8\uC73C\uB85C(\uB85C)";
            string expected = "\uAC80\uC740 \uC0AC\uACFC\uB97C \uC11C\uC6B8\uB85C";
            Assert.That(KoreanParticles.Resolve(written), Is.EqualTo(expected));
        }

        [Test]
        public void TextWithoutAParticlePair_IsReturnedUnchanged()
        {
            const string plain = "\uC0AC\uACFC (3)";
            Assert.That(KoreanParticles.Resolve(plain), Is.SameAs(plain));
            Assert.That(KoreanParticles.Resolve(null), Is.Null);
            Assert.That(KoreanParticles.Resolve(string.Empty), Is.Empty);
        }
    }
}
