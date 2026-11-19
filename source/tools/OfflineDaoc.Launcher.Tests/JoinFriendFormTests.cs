#nullable enable
using System.Reflection;
using NUnit.Framework;

namespace OfflineDaoc.Launcher.Tests;

// Living Server 1.2 (owner, 2026-11-08): Join Friend asks for the host password and the friend's own password.
[TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
public sealed class JoinFriendFormTests
{
    [Test]
    public void TheFormHasAHostPasswordBoxAndAnOwnPasswordBox()
    {
        string runtime = Path.Combine(Path.GetTempPath(), "odc-joinform-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runtime);
        try
        {
            Type type = Assembly.Load("OfflineDAoC").GetType("OfflineDaoc.Launcher.JoinFriendForm")!;
            using var form = (Form)Activator.CreateInstance(type, runtime)!;
            var (host, own) = ((TextBox, TextBox))type.GetProperty("PasswordBoxes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;

            Assert.That(host, Is.Not.SameAs(own));
            Assert.That(host.UseSystemPasswordChar && own.UseSystemPasswordChar, Is.True, "both are hidden");
            Assert.That(host.MaxLength, Is.EqualTo(20));
            Assert.That(own.MaxLength, Is.EqualTo(20));
            Assert.That(host.Text, Is.Empty, "the host password is never remembered");
            List<string> captions = Descendants(form).OfType<Label>().Select(label => label.Text).ToList();
            Assert.That(captions.Any(text => text.StartsWith("Host password", StringComparison.Ordinal)), Is.True);
            Assert.That(captions.Any(text => text.StartsWith("Your own password", StringComparison.Ordinal)), Is.True);
        }
        finally
        {
            Directory.Delete(runtime, true);
        }
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control nested in Descendants(child))
                yield return nested;
        }
    }
}
