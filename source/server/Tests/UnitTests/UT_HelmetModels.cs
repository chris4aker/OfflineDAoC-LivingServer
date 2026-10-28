using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>Helmets the client draws nothing for take a visible stand-in of their realm and armour type (owner 2026-10-19).</summary>
[TestFixture]
public class UT_HelmetModels
{
    [TestCase(822, eRealm.Albion, eObjectType.Cloth, 823)]
    [TestCase(511, eRealm.Albion, eObjectType.Cloth, 823)]
    [TestCase(35, eRealm.Albion, eObjectType.Leather, 62)]
    [TestCase(3861, eRealm.Albion, eObjectType.Plate, 93)]
    [TestCase(511, eRealm.Midgard, eObjectType.Cloth, 825)]
    [TestCase(3865, eRealm.Midgard, eObjectType.Chain, 832)]
    [TestCase(511, eRealm.Hibernia, eObjectType.Reinforced, 835)]
    [TestCase(3867, eRealm.Hibernia, eObjectType.Scale, 839)]
    [TestCase(596, eRealm.None, eObjectType.Chain, 1236)]
    public void InvisibleHelmetTakesAVisibleOne(int model, eRealm realm, eObjectType type, int expected) =>
        Assert.That(HelmetModels.Visible(model, realm, type), Is.EqualTo(expected));

    [TestCase(823)]
    [TestCase(1278)]
    [TestCase(826)]
    public void VisibleHelmetKeepsItsModel(int model) =>
        Assert.That(HelmetModels.Visible(model, eRealm.Albion, eObjectType.Cloth), Is.EqualTo(model));
}
