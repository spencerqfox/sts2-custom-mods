using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization.Fonts;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.addons.mega_text;

namespace JoinInProgress.Join;

internal sealed partial class LateJoinCharacterPicker : Control
{
    private readonly TaskCompletionSource<CharacterModel?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenRegistration _cancellationRegistration;
    private IReadOnlyList<CharacterModel> _characters = null!;

    public static Task<CharacterModel?> Pick(
        IReadOnlyList<CharacterModel> characters,
        CancellationToken cancellationToken)
    {
        LateJoinCharacterPicker picker = new()
        {
            Name = "LateJoinCharacterPicker",
            _characters = characters
        };
        (NGame.Instance ?? throw new InvalidOperationException("The game root is unavailable."))
            .AddChildSafely(picker);
        picker._cancellationRegistration = cancellationToken.Register(() => picker.Finish(null));
        return picker._completion.Task;
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;
        ZIndex = 1000;

        ColorRect backstop = new()
        {
            Color = new Color(0.015f, 0.02f, 0.03f, 0.94f),
            MouseFilter = MouseFilterEnum.Stop
        };
        backstop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(backstop);

        PanelContainer panel = new();
        panel.AnchorLeft = 0.5f;
        panel.AnchorTop = 0.5f;
        panel.AnchorRight = 0.5f;
        panel.AnchorBottom = 0.5f;
        panel.OffsetLeft = -430f;
        panel.OffsetTop = -290f;
        panel.OffsetRight = 430f;
        panel.OffsetBottom = 290f;
        panel.AddThemeStyleboxOverride("panel", CreatePanelStyle());
        AddChild(panel);

        MarginContainer margin = new();
        margin.AddThemeConstantOverride("margin_left", 42);
        margin.AddThemeConstantOverride("margin_top", 34);
        margin.AddThemeConstantOverride("margin_right", 42);
        margin.AddThemeConstantOverride("margin_bottom", 34);
        panel.AddChild(margin);

        VBoxContainer content = new();
        content.AddThemeConstantOverride("separation", 18);
        margin.AddChild(content);

        Label title = NewLabel("JOIN IN PROGRESS", 34, StsColors.gold);
        content.AddChild(title);
        content.AddChild(NewLabel(
            "Choose a character. You will replay the party's completed rooms before the next floor begins.",
            20,
            StsColors.cream));

        GridContainer grid = new() { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 14);
        grid.AddThemeConstantOverride("v_separation", 14);
        content.AddChild(grid);

        foreach (CharacterModel character in _characters)
        {
            Button button = NewButton(character.Title.GetFormattedText());
            button.CustomMinimumSize = new Vector2(360f, 72f);
            button.Pressed += () => Finish(character);
            grid.AddChild(button);
        }

        Control spacer = new() { CustomMinimumSize = new Vector2(0f, 8f) };
        content.AddChild(spacer);

        Button cancel = NewButton("Cancel");
        cancel.CustomMinimumSize = new Vector2(220f, 58f);
        cancel.Pressed += () => Finish(null);
        content.AddChild(cancel);

        grid.GetChildren().OfType<Button>().FirstOrDefault()?.GrabFocus();
    }

    private void Finish(CharacterModel? character)
    {
        if (!_completion.TrySetResult(character))
        {
            return;
        }

        _cancellationRegistration.Dispose();
        this.QueueFreeSafely();
    }

    public override void _ExitTree()
    {
        _cancellationRegistration.Dispose();
        _completion.TrySetResult(null);
    }

    private static Label NewLabel(string text, int fontSize, Color color)
    {
        Label label = new()
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        label.AddThemeFontSizeOverride(ThemeConstants.Label.FontSize, fontSize);
        label.AddThemeColorOverride(ThemeConstants.Label.FontColor, color);
        label.ApplyLocaleFontSubstitution(FontType.Regular, ThemeConstants.Label.Font);
        return label;
    }

    internal static Button NewButton(string text)
    {
        Button button = new()
        {
            Text = text,
            FocusMode = FocusModeEnum.All,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand
        };
        button.AddThemeFontSizeOverride(ThemeConstants.Label.FontSize, 21);
        button.ApplyLocaleFontSubstitution(FontType.Regular, ThemeConstants.Label.Font);
        button.AddThemeStyleboxOverride("normal", CreateButtonStyle(new Color(0.09f, 0.12f, 0.16f)));
        button.AddThemeStyleboxOverride("hover", CreateButtonStyle(new Color(0.14f, 0.19f, 0.25f)));
        button.AddThemeStyleboxOverride("focus", CreateButtonStyle(new Color(0.16f, 0.22f, 0.29f), new Color(0.93f, 0.77f, 0.36f)));
        button.AddThemeStyleboxOverride("pressed", CreateButtonStyle(new Color(0.06f, 0.09f, 0.12f)));
        return button;
    }

    private static StyleBoxFlat CreatePanelStyle()
    {
        return new StyleBoxFlat
        {
            BgColor = new Color(0.045f, 0.055f, 0.075f, 0.99f),
            BorderColor = new Color(1f, 1f, 1f, 0.13f),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 18,
            CornerRadiusTopRight = 18,
            CornerRadiusBottomLeft = 18,
            CornerRadiusBottomRight = 18
        };
    }

    private static StyleBoxFlat CreateButtonStyle(Color background, Color? border = null)
    {
        Color borderColor = border ?? new Color(1f, 1f, 1f, 0.12f);
        return new StyleBoxFlat
        {
            BgColor = background,
            BorderColor = borderColor,
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 10,
            CornerRadiusTopRight = 10,
            CornerRadiusBottomLeft = 10,
            CornerRadiusBottomRight = 10,
            ContentMarginLeft = 16f,
            ContentMarginRight = 16f,
            ContentMarginTop = 12f,
            ContentMarginBottom = 12f
        };
    }
}
