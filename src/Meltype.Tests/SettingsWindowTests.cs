// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 hirokawa-beach

using Meltype.Config;
using Meltype.UI;

namespace Meltype.Tests;

internal static class SettingsWindowTests
{
    [Test]
    public static void Settings_ShowsAndRecoversWhenHiddenOrMinimized()
    {
        using var engine = new MeltypeEngine(new Settings(), null, null, null);
        using var form = new SettingsForm(engine);
        try
        {
            form.ShowForUser();
            Application.DoEvents();
            Assert.True(Native.IsWindowVisible(form.Handle), "初回表示");
            form.Hide();
            form.ShowForUser();
            Application.DoEvents();
            Assert.True(Native.IsWindowVisible(form.Handle), "非表示からの再表示");
            form.WindowState = FormWindowState.Minimized;
            form.ShowForUser();
            Application.DoEvents();
            Assert.Equal(FormWindowState.Normal, form.WindowState, "最小化から復帰");
            Assert.True(Native.IsWindowVisible(form.Handle), "復帰後の実ウィンドウ");
        }
        finally { form.CancelButton!.PerformClick(); }
    }
}
