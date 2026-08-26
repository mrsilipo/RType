using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RType.Core;
using RType.World;

namespace RType.Ui;

public sealed class MenuRenderer : IDisposable
{
    private const string MainMenuBackgroundPath = "Assets/Menus/Backgrounds/MainMenu/MainMenu_Background.png";
    private const string RTypeLogoPath = "Assets/Menus/Backgrounds/MainMenu/MainMenu_Logo_RType.png";
    private const string MainMenuOptionActivePath = "Assets/Menus/Backgrounds/MainMenu/MainMenu_MenuOption_Active.png";
    private const string MainMenuOptionInactivePath = "Assets/Menus/Backgrounds/MainMenu/MainMenu_MenuOption_InActive.png";
    private const string MainMenuWedgePath = "Assets/Menus/Backgrounds/MainMenu/MainMenu_UIPanel_RedWedge.png";
    private const string ArcadeMenuBackgroundPath = "Assets/Menus/Backgrounds/ArcadeModeMenu/Background.png";
    private const string ArcadeMenuLogoPath = "Assets/Menus/Backgrounds/ArcadeModeMenu/ArcadeModeMenu_Logo_RType.png";
    private const string ArcadeMenuWedgePath = "Assets/Menus/Backgrounds/ArcadeModeMenu/ArcadeModeMenu_UIPanel_RedWedge.png";
    private const int MainMenuLowerBandTop = 679;
    private const int MainMenuThinSeparatorTop = 656;
    private const int MainMenuThinSeparatorHeight = 4;
    private const int MainMenuThickSeparatorTop = 669;
    private const int MainMenuThickSeparatorHeight = 10;
    private const float MainMenuWedgeAnimationSeconds = 0.45f;
    private const float MainMenuOptionFadeSeconds = 0.14f;
    private const float MainMenuSelectionSlideSeconds = 0.14f;
    private const float MainMenuSelectionSlidePixels = 25f;
    private const int MainMenuWedgeWidth = 713;
    private const int MainMenuWedgeHeight = 1119;
    private const int MainMenuLeftWedgeHiddenX = -MainMenuWedgeWidth;
    private const int MainMenuLeftWedgeShownX = -539;
    private const int MainMenuRightWedgeHiddenX = UiLayout.Width;
    private const int MainMenuRightWedgeShownX = 1560;
    private static readonly Rectangle RTypeLogoBounds = new(293, 146, 785, 303);
    private static readonly Rectangle MainMenuActiveOptionBounds = new(359, 747, 569, 48);
    private static readonly Rectangle MainMenuInactiveOptionBounds = new(359, 817, 108, 48);
    private const int MainMenuRowStep = 70;
    private const float MainMenuTextX = 488f;
    private const float MainMenuTextYOffset = -3f;
    private const float ArcadeMenuAnimationSeconds = 0.45f;
    private const float ArcadeMenuOptionFadeSeconds = 0.14f;
    private const float ArcadeMenuSelectionSlideSeconds = 0.14f;
    private const float ArcadeMenuSelectionSlidePixels = 22f;
    private const int ArcadeMenuPanelLeft = 566;
    private const int ArcadeMenuThinSeparatorX = 546;
    private const int ArcadeMenuThinSeparatorWidth = 4;
    private const int ArcadeMenuThickSeparatorX = 560;
    private const int ArcadeMenuThickSeparatorWidth = 10;
    private const int ArcadeMenuWedgeWidth = 1545;
    private const int ArcadeMenuWedgeHeight = 784;
    private const int ArcadeMenuTopWedgeX = 0;
    private const int ArcadeMenuTopWedgeShownY = -654;
    private const int ArcadeMenuTopWedgeHiddenY = -870;
    private const int ArcadeMenuBottomWedgeX = 376;
    private const int ArcadeMenuBottomWedgeShownY = 745;
    private const int ArcadeMenuBottomWedgeHiddenY = 1080;
    private static readonly Rectangle ArcadeMenuLogoBounds = new(1608, 948, 293, 114);
    private static readonly Rectangle ArcadeMenuActiveOptionBounds = new(676, 492, 569, 48);
    private static readonly Rectangle ArcadeMenuInactiveOptionBounds = new(676, 562, 108, 48);
    private const int ArcadeMenuRowStep = 70;
    private const float ArcadeMenuTextX = 808f;
    private const float ArcadeMenuTextYOffset = 1f;
    private const int ArcadeMenuTextWeight = 520;

    private static readonly Color PanelFill = new(5, 8, 12, 190);
    private static readonly Color PanelStroke = new(238, 238, 230, 92);
    private static readonly Color TextPrimary = new(246, 246, 238, 245);
    private static readonly Color TextMuted = new(168, 178, 180, 228);
    private static readonly Color BrandRed = new(227, 0, 0, 255);
    private static readonly Color MainMenuLowerBandFill = new(0, 0, 0, 128);
    private static readonly Color ArcadeMenuPanelFill = new(0, 0, 0, 168);
    private static readonly Color MenuInactiveText = new(160, 152, 149, 255);

    private static readonly string[] MainMenuItems =
    [
        "Arcade Mode",
        "Engine Sim Room",
        "Career Mode",
        "Options",
        "Quit"
    ];
    private static readonly string[] ArcadeMenuItems =
    [
        "Single Race",
        "Time Trial",
        "2 Player Battle",
        "Back"
    ];
    private readonly Texture2D _pixel;
    private readonly Texture2D _mainMenuBackground;
    private readonly Texture2D _arcadeMenuBackground;
    private readonly Texture2D _rtypeLogo;
    private readonly Texture2D _arcadeLogo;
    private readonly Texture2D _mainMenuOptionActive;
    private readonly Texture2D _mainMenuOptionInactive;
    private readonly Texture2D _mainMenuWedge;
    private readonly Texture2D _arcadeMenuWedge;
    private readonly RuntimeFontTextureCache _fonts;
    private float _mainMenuPresentationSeconds;
    private float _mainMenuSelectionSlideSeconds;
    private int _lastMainMenuSelectedIndex = -1;
    private float _arcadeMenuPresentationSeconds;
    private float _arcadeMenuSelectionSlideSeconds;
    private int _lastArcadeMenuSelectedIndex = -1;

    public MenuRenderer(GraphicsDevice graphicsDevice)
    {
        _pixel = new Texture2D(graphicsDevice, 1, 1);
        _pixel.SetData([Color.White]);
        _mainMenuBackground = LoadTextureOrCreateFallback(graphicsDevice, MainMenuBackgroundPath);
        _arcadeMenuBackground = LoadTextureOrCreateFallback(graphicsDevice, ArcadeMenuBackgroundPath);
        _rtypeLogo = LoadTextureOrCreateFallback(graphicsDevice, RTypeLogoPath);
        _arcadeLogo = LoadTextureOrCreateFallback(graphicsDevice, ArcadeMenuLogoPath);
        _mainMenuOptionActive = LoadTextureOrCreateFallback(graphicsDevice, MainMenuOptionActivePath);
        _mainMenuOptionInactive = LoadTextureOrCreateFallback(graphicsDevice, MainMenuOptionInactivePath);
        _mainMenuWedge = LoadTextureOrCreateFallback(graphicsDevice, MainMenuWedgePath);
        _arcadeMenuWedge = LoadTextureOrCreateFallback(graphicsDevice, ArcadeMenuWedgePath);
        _fonts = new RuntimeFontTextureCache(graphicsDevice, new TachometerFontConfig());
    }

    public void Update(
        GameTime gameTime,
        bool mainMenuActive,
        int selectedIndex,
        bool arcadeMenuActive,
        int arcadeSelectedIndex)
    {
        float elapsedSeconds = (float)gameTime.ElapsedGameTime.TotalSeconds;
        UpdateMainMenuAnimation(elapsedSeconds, mainMenuActive, selectedIndex);
        UpdateArcadeMenuAnimation(elapsedSeconds, arcadeMenuActive, arcadeSelectedIndex);
    }

    private void UpdateMainMenuAnimation(float elapsedSeconds, bool active, int selectedIndex)
    {
        if (!active)
        {
            _mainMenuPresentationSeconds = 0f;
            _mainMenuSelectionSlideSeconds = MainMenuSelectionSlideSeconds;
            _lastMainMenuSelectedIndex = -1;
            return;
        }

        int safeSelectedIndex = Math.Clamp(selectedIndex, 0, MainMenuItems.Length - 1);
        if (_lastMainMenuSelectedIndex != safeSelectedIndex)
        {
            _lastMainMenuSelectedIndex = safeSelectedIndex;
            _mainMenuSelectionSlideSeconds = 0f;
        }
        else
        {
            _mainMenuSelectionSlideSeconds = MathF.Min(
                _mainMenuSelectionSlideSeconds + elapsedSeconds,
                MainMenuSelectionSlideSeconds);
        }

        _mainMenuPresentationSeconds = MathF.Min(
            _mainMenuPresentationSeconds + elapsedSeconds,
            MainMenuWedgeAnimationSeconds + MainMenuOptionFadeSeconds);
    }

    private void UpdateArcadeMenuAnimation(float elapsedSeconds, bool active, int selectedIndex)
    {
        if (!active)
        {
            _arcadeMenuPresentationSeconds = 0f;
            _arcadeMenuSelectionSlideSeconds = ArcadeMenuSelectionSlideSeconds;
            _lastArcadeMenuSelectedIndex = -1;
            return;
        }

        int safeSelectedIndex = Math.Clamp(selectedIndex, 0, ArcadeMenuItems.Length - 1);
        if (_lastArcadeMenuSelectedIndex != safeSelectedIndex)
        {
            _lastArcadeMenuSelectedIndex = safeSelectedIndex;
            _arcadeMenuSelectionSlideSeconds = 0f;
        }
        else
        {
            _arcadeMenuSelectionSlideSeconds = MathF.Min(
                _arcadeMenuSelectionSlideSeconds + elapsedSeconds,
                ArcadeMenuSelectionSlideSeconds);
        }

        _arcadeMenuPresentationSeconds = MathF.Min(
            _arcadeMenuPresentationSeconds + elapsedSeconds,
            ArcadeMenuAnimationSeconds + ArcadeMenuOptionFadeSeconds);
    }

    public void DrawMain(SpriteBatch spriteBatch, int selectedIndex)
    {
        int safeSelectedIndex = Math.Clamp(selectedIndex, 0, MainMenuItems.Length - 1);
        spriteBatch.Draw(_mainMenuBackground, new Rectangle(0, 0, UiLayout.Width, UiLayout.Height), Color.White);
        int panelOffsetY = GetMainMenuPanelOffsetY();
        DrawMainMenuFraming(spriteBatch, panelOffsetY);
        DrawMainMenuWedges(spriteBatch);
        spriteBatch.Draw(_rtypeLogo, RTypeLogoBounds, Color.White);
        DrawMainMenuRows(spriteBatch, safeSelectedIndex);
    }

    private void DrawArcadeMode(SpriteBatch spriteBatch, int selectedIndex)
    {
        int safeSelectedIndex = Math.Clamp(selectedIndex, 0, ArcadeMenuItems.Length - 1);
        spriteBatch.Draw(_arcadeMenuBackground, new Rectangle(0, 0, UiLayout.Width, UiLayout.Height), Color.White);
        DrawArcadeMenuFraming(spriteBatch);
        DrawArcadeMenuBanners(spriteBatch);

        float contentAlpha = GetArcadeMenuOptionAlpha();
        if (contentAlpha <= 0.001f)
        {
            return;
        }

        DrawCentered(
            spriteBatch,
            "ARCADE MODE",
            new Vector2(1016f, 354f),
            78f,
            900,
            TextPrimary * contentAlpha);
        DrawArcadeMenuRows(spriteBatch, safeSelectedIndex, contentAlpha);
    }

    public static bool TryHitMainMenuItem(Vector2 uiPosition, out int index)
    {
        const int hitX = 340;
        const int hitWidth = 640;
        const int hitHeight = 58;
        for (int i = 0; i < MainMenuItems.Length; i++)
        {
            Rectangle bounds = new(hitX, MainMenuActiveOptionBounds.Y + i * MainMenuRowStep - 5, hitWidth, hitHeight);
            if (bounds.Contains((int)uiPosition.X, (int)uiPosition.Y))
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    public static bool TryHitArcadeMenuItem(Vector2 uiPosition, out int index)
    {
        const int hitX = 650;
        const int hitWidth = 650;
        const int hitHeight = 58;
        for (int i = 0; i < ArcadeMenuItems.Length; i++)
        {
            Rectangle bounds = new(hitX, ArcadeMenuActiveOptionBounds.Y + i * ArcadeMenuRowStep - 5, hitWidth, hitHeight);
            if (bounds.Contains((int)uiPosition.X, (int)uiPosition.Y))
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    public static bool TryHitListItem(Vector2 uiPosition, int itemCount, int startY, out int index)
    {
        const int panelWidth = 720;
        const int itemHeight = 78;
        int panelX = (UiLayout.Width - panelWidth) / 2;
        int y0 = NormalizeLegacyY(startY);
        for (int i = 0; i < itemCount; i++)
        {
            Rectangle bounds = new(panelX, y0 + i * 92, panelWidth, itemHeight);
            if (bounds.Contains((int)uiPosition.X, (int)uiPosition.Y))
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    public static bool TryHitPopupItem(Vector2 uiPosition, int itemCount, out int index)
    {
        const int popupWidth = 720;
        const int popupHeight = 236;
        int popupX = (UiLayout.Width - popupWidth) / 2;
        int popupY = (UiLayout.Height - popupHeight) / 2;
        for (int i = 0; i < itemCount; i++)
        {
            Rectangle bounds = new(popupX + 42 + i * 320, popupY + 122, 280, 70);
            if (bounds.Contains((int)uiPosition.X, (int)uiPosition.Y))
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    public void DrawEvent(SpriteBatch spriteBatch, int selectedIndex)
    {
        DrawArcadeMode(spriteBatch, selectedIndex);
    }

    public void DrawCarSelect(
        SpriteBatch spriteBatch,
        string[] cars,
        int selectedIndex,
        bool showTransmissionPopup,
        int transmissionIndex)
    {
        DrawHeader(spriteBatch, "SELECT CAR");
        DrawList(spriteBatch, cars, selectedIndex, 118);

        if (showTransmissionPopup)
        {
            DrawPopup(spriteBatch, "TRANSMISSION", ["AUTOMATIC", "MANUAL"], transmissionIndex);
        }
    }

    public void DrawTrackSelect(
        SpriteBatch spriteBatch,
        IReadOnlyList<TrackDefinition> tracks,
        int selectedIndex,
        bool showDirectionPopup,
        int directionIndex)
    {
        TrackDefinition track = tracks[selectedIndex];
        DrawHeader(spriteBatch, "SELECT TRACK");

        DrawPanel(spriteBatch, 120, 214, 760, 310, new Color(5, 8, 12, 178));
        DrawText(spriteBatch, track.DisplayName, 152, 246, 38f, 760, TextPrimary);
        DrawText(spriteBatch, $"LENGTH {track.LengthMeters} M", 156, 328, 24f, 600, TextMuted);
        DrawText(spriteBatch, $"STRAIGHT {track.LongestStraightMeters} M", 156, 374, 24f, 600, TextMuted);
        DrawText(spriteBatch, $"HEIGHT {track.ElevationDifferenceMeters:0.0} M", 156, 420, 24f, 600, TextMuted);

        DrawTrackMap(spriteBatch, track, new Rectangle(1060, 202, 620, 392));
        DrawList(spriteBatch, [track.DisplayName], 0, 185);

        if (showDirectionPopup)
        {
            DrawPopup(spriteBatch, "DIRECTION", ["NORMAL", "REVERSE"], directionIndex);
        }
    }

    public void DrawCountdown(SpriteBatch spriteBatch, string text)
    {
        float size = text == "GO" ? 164f : 210f;
        DrawCentered(spriteBatch, text, new Vector2(UiLayout.Width * 0.5f, 422f), size, 850, new Color(250, 235, 142, 245));
    }

    public void DrawResults(SpriteBatch spriteBatch, RaceSessionState session, int selectedIndex)
    {
        DrawHeader(spriteBatch, "RESULTS");
        DrawPanel(spriteBatch, 136, 220, 760, 292, new Color(5, 8, 12, 204));
        DrawText(spriteBatch, $"TOTAL {FormatTime(session.RaceTime)}", 172, 258, 30f, 700, TextPrimary);
        DrawText(spriteBatch, $"LAPS {session.CompletedLaps}/{session.TargetLaps}", 172, 316, 25f, 620, TextMuted);

        string bestLap = session.BestLapTime is TimeSpan best ? FormatTime(best) : "--:--.--";
        string lastLap = session.LastLapTime is TimeSpan last ? FormatTime(last) : "--:--.--";
        Color lastLapColor = session.LastLapWasValid ? TextMuted : new Color(250, 235, 142, 238);
        DrawText(spriteBatch, $"BEST {bestLap}", 172, 390, 25f, 620, new Color(220, 244, 206, 238));
        DrawText(spriteBatch, $"LAST {lastLap} {(session.LastLapWasValid ? "VALID" : "INVALID")}", 172, 438, 25f, 620, lastLapColor);

        DrawList(spriteBatch, ["RETRY", "TRACK SELECT"], selectedIndex, 175);
    }

    public void Dispose()
    {
        _fonts.Dispose();
        _arcadeMenuWedge.Dispose();
        _mainMenuWedge.Dispose();
        _mainMenuOptionInactive.Dispose();
        _mainMenuOptionActive.Dispose();
        _arcadeLogo.Dispose();
        _rtypeLogo.Dispose();
        _arcadeMenuBackground.Dispose();
        _mainMenuBackground.Dispose();
        _pixel.Dispose();
    }

    private void DrawArcadeMenuFraming(SpriteBatch spriteBatch)
    {
        int offsetX = GetArcadeMenuPanelOffsetX();
        int panelX = ArcadeMenuPanelLeft + offsetX;
        if (panelX < UiLayout.Width)
        {
            spriteBatch.Draw(
                _pixel,
                new Rectangle(panelX, 0, UiLayout.Width - panelX, UiLayout.Height),
                ArcadeMenuPanelFill);
        }

        DrawTranslatedVerticalBar(spriteBatch, ArcadeMenuThinSeparatorX + offsetX, ArcadeMenuThinSeparatorWidth, BrandRed);
        DrawTranslatedVerticalBar(spriteBatch, ArcadeMenuThickSeparatorX + offsetX, ArcadeMenuThickSeparatorWidth, BrandRed);
    }

    private void DrawArcadeMenuBanners(SpriteBatch spriteBatch)
    {
        float progress = MathHelper.Clamp(_arcadeMenuPresentationSeconds / ArcadeMenuAnimationSeconds, 0f, 1f);
        float eased = EaseOutCubic(progress);
        int topY = (int)MathF.Round(MathHelper.Lerp(ArcadeMenuTopWedgeHiddenY, ArcadeMenuTopWedgeShownY, eased));
        int bottomY = (int)MathF.Round(MathHelper.Lerp(ArcadeMenuBottomWedgeHiddenY, ArcadeMenuBottomWedgeShownY, eased));
        int logoOffsetY = bottomY - ArcadeMenuBottomWedgeShownY;

        spriteBatch.Draw(
            _arcadeMenuWedge,
            new Rectangle(ArcadeMenuTopWedgeX, topY, ArcadeMenuWedgeWidth, ArcadeMenuWedgeHeight),
            Color.White);

        spriteBatch.Draw(
            _arcadeMenuWedge,
            new Rectangle(ArcadeMenuBottomWedgeX, bottomY, ArcadeMenuWedgeWidth, ArcadeMenuWedgeHeight),
            Color.White);

        spriteBatch.Draw(
            _arcadeLogo,
            new Rectangle(
                ArcadeMenuLogoBounds.X,
                ArcadeMenuLogoBounds.Y + logoOffsetY,
                ArcadeMenuLogoBounds.Width,
                ArcadeMenuLogoBounds.Height),
            Color.White * eased);
    }

    private void DrawArcadeMenuRows(SpriteBatch spriteBatch, int selectedIndex, float alpha)
    {
        float selectionSlideProgress = ArcadeMenuSelectionSlideSeconds <= 0f
            ? 1f
            : MathHelper.Clamp(_arcadeMenuSelectionSlideSeconds / ArcadeMenuSelectionSlideSeconds, 0f, 1f);
        int activeOffsetX = (int)MathF.Round(ArcadeMenuSelectionSlidePixels * EaseOutCubic(selectionSlideProgress));
        Color optionTint = Color.White * alpha;
        Color activeTextColor = BrandRed * alpha;
        Color inactiveTextColor = MenuInactiveText * alpha;

        for (int i = 0; i < ArcadeMenuItems.Length; i++)
        {
            int rowY = ArcadeMenuActiveOptionBounds.Y + i * ArcadeMenuRowStep;
            bool selected = i == selectedIndex;
            int offsetX = selected ? activeOffsetX : 0;
            Rectangle bounds = selected
                ? new Rectangle(
                    ArcadeMenuActiveOptionBounds.X + offsetX,
                    rowY,
                    ArcadeMenuActiveOptionBounds.Width,
                    ArcadeMenuActiveOptionBounds.Height)
                : new Rectangle(
                    ArcadeMenuInactiveOptionBounds.X,
                    rowY,
                    ArcadeMenuInactiveOptionBounds.Width,
                    ArcadeMenuInactiveOptionBounds.Height);

            spriteBatch.Draw(selected ? _mainMenuOptionActive : _mainMenuOptionInactive, bounds, optionTint);
            DrawText(
                spriteBatch,
                ArcadeMenuItems[i],
                ArcadeMenuTextX + offsetX,
                rowY + ArcadeMenuTextYOffset,
                31f,
                ArcadeMenuTextWeight,
                selected ? activeTextColor : inactiveTextColor);
        }
    }

    private void DrawHeader(SpriteBatch spriteBatch, string title)
    {
        spriteBatch.Draw(_mainMenuBackground, new Rectangle(0, 0, UiLayout.Width, UiLayout.Height), Color.White);
        spriteBatch.Draw(_pixel, new Rectangle(0, 0, UiLayout.Width, UiLayout.Height), new Color(2, 5, 9, 210));
        DrawText(spriteBatch, title, 92, 80, 58f, 820, TextPrimary);
        spriteBatch.Draw(_pixel, new Rectangle(96, 164, 520, 4), BrandRed);
    }

    private void DrawMainMenuFraming(SpriteBatch spriteBatch, int offsetY)
    {
        int panelTop = MainMenuLowerBandTop + offsetY;
        if (panelTop < UiLayout.Height)
        {
            spriteBatch.Draw(
                _pixel,
                new Rectangle(0, panelTop, UiLayout.Width, UiLayout.Height - MainMenuLowerBandTop),
                MainMenuLowerBandFill);
        }

        DrawTranslatedHorizontalBar(spriteBatch, MainMenuThinSeparatorTop + offsetY, MainMenuThinSeparatorHeight, BrandRed);
        DrawTranslatedHorizontalBar(spriteBatch, MainMenuThickSeparatorTop + offsetY, MainMenuThickSeparatorHeight, BrandRed);
    }

    private void DrawMainMenuWedges(SpriteBatch spriteBatch)
    {
        float normalizedTime = MainMenuWedgeAnimationSeconds <= 0f
            ? 1f
            : MathHelper.Clamp(_mainMenuPresentationSeconds / MainMenuWedgeAnimationSeconds, 0f, 1f);
        float eased = EaseOutCubic(normalizedTime);
        int leftX = (int)MathF.Round(MathHelper.Lerp(MainMenuLeftWedgeHiddenX, MainMenuLeftWedgeShownX, eased));
        int rightX = (int)MathF.Round(MathHelper.Lerp(MainMenuRightWedgeHiddenX, MainMenuRightWedgeShownX, eased));

        spriteBatch.Draw(
            _mainMenuWedge,
            new Rectangle(leftX, 0, MainMenuWedgeWidth, MainMenuWedgeHeight),
            Color.White);
        spriteBatch.Draw(
            _mainMenuWedge,
            new Rectangle(rightX, 0, MainMenuWedgeWidth, MainMenuWedgeHeight),
            Color.White);
    }

    private void DrawMainMenuRows(SpriteBatch spriteBatch, int selectedIndex)
    {
        float optionAlpha = GetMainMenuOptionAlpha();
        if (optionAlpha <= 0.001f)
        {
            return;
        }

        float selectionSlideProgress = MainMenuSelectionSlideSeconds <= 0f
            ? 1f
            : MathHelper.Clamp(_mainMenuSelectionSlideSeconds / MainMenuSelectionSlideSeconds, 0f, 1f);
        int activeOffsetX = (int)MathF.Round(MainMenuSelectionSlidePixels * EaseOutCubic(selectionSlideProgress));
        Color optionTint = Color.White * optionAlpha;
        Color activeTextColor = BrandRed * optionAlpha;
        Color inactiveTextColor = MenuInactiveText * optionAlpha;

        for (int i = 0; i < MainMenuItems.Length; i++)
        {
            int rowY = MainMenuActiveOptionBounds.Y + i * MainMenuRowStep;
            bool selected = i == selectedIndex;
            int offsetX = selected ? activeOffsetX : 0;
            Rectangle bounds = selected
                ? new Rectangle(MainMenuActiveOptionBounds.X + offsetX, rowY, MainMenuActiveOptionBounds.Width, MainMenuActiveOptionBounds.Height)
                : new Rectangle(MainMenuInactiveOptionBounds.X, rowY, MainMenuInactiveOptionBounds.Width, MainMenuInactiveOptionBounds.Height);

            spriteBatch.Draw(selected ? _mainMenuOptionActive : _mainMenuOptionInactive, bounds, optionTint);
            DrawText(
                spriteBatch,
                MainMenuItems[i],
                MainMenuTextX + offsetX,
                rowY + MainMenuTextYOffset,
                40f,
                500,
                selected ? activeTextColor : inactiveTextColor);
        }
    }

    private void DrawTranslatedHorizontalBar(SpriteBatch spriteBatch, int y, int height, Color color)
    {
        int top = Math.Max(y, 0);
        int bottom = Math.Min(y + height, UiLayout.Height);
        if (bottom <= top)
        {
            return;
        }

        spriteBatch.Draw(_pixel, new Rectangle(0, top, UiLayout.Width, bottom - top), color);
    }

    private void DrawTranslatedVerticalBar(SpriteBatch spriteBatch, int x, int width, Color color)
    {
        int left = Math.Max(x, 0);
        int right = Math.Min(x + width, UiLayout.Width);
        if (right <= left)
        {
            return;
        }

        spriteBatch.Draw(_pixel, new Rectangle(left, 0, right - left, UiLayout.Height), color);
    }

    private int GetMainMenuPanelOffsetY()
    {
        float progress = MathHelper.Clamp(_mainMenuPresentationSeconds / MainMenuWedgeAnimationSeconds, 0f, 1f);
        float eased = EaseOutCubic(progress);
        int hiddenOffsetY = UiLayout.Height - MainMenuThinSeparatorTop;
        return (int)MathF.Round(hiddenOffsetY * (1f - eased));
    }

    private int GetArcadeMenuPanelOffsetX()
    {
        float progress = MathHelper.Clamp(_arcadeMenuPresentationSeconds / ArcadeMenuAnimationSeconds, 0f, 1f);
        float eased = EaseOutCubic(progress);
        int hiddenOffsetX = UiLayout.Width - ArcadeMenuPanelLeft;
        return (int)MathF.Round(hiddenOffsetX * (1f - eased));
    }

    private float GetMainMenuOptionAlpha()
    {
        float progress = MathHelper.Clamp(
            (_mainMenuPresentationSeconds - MainMenuWedgeAnimationSeconds) / MainMenuOptionFadeSeconds,
            0f,
            1f);
        return EaseOutCubic(progress);
    }

    private float GetArcadeMenuOptionAlpha()
    {
        float progress = MathHelper.Clamp(
            (_arcadeMenuPresentationSeconds - ArcadeMenuAnimationSeconds) / ArcadeMenuOptionFadeSeconds,
            0f,
            1f);
        return EaseOutCubic(progress);
    }

    private static float EaseOutCubic(float value)
    {
        float inverse = 1f - value;
        return 1f - inverse * inverse * inverse;
    }

    private void DrawList(SpriteBatch spriteBatch, string[] items, int selectedIndex, int startY)
    {
        const int panelWidth = 720;
        const int itemHeight = 78;
        int panelX = (UiLayout.Width - panelWidth) / 2;
        int y0 = NormalizeLegacyY(startY);
        for (int i = 0; i < items.Length; i++)
        {
            Rectangle bounds = new(panelX, y0 + i * 92, panelWidth, itemHeight);
            DrawMainMenuItem(spriteBatch, items[i], i == selectedIndex, bounds);
        }
    }

    private void DrawMainMenuItem(SpriteBatch spriteBatch, string text, bool selected, Rectangle bounds)
    {
        Color fill = selected ? new Color(12, 15, 18, 222) : new Color(4, 7, 11, 156);
        Color stroke = selected ? new Color(255, 255, 246, 130) : new Color(210, 218, 218, 54);
        spriteBatch.Draw(_pixel, bounds, fill);
        spriteBatch.Draw(_pixel, new Rectangle(bounds.X, bounds.Y, bounds.Width, 2), stroke);
        spriteBatch.Draw(_pixel, new Rectangle(bounds.X, bounds.Bottom - 2, bounds.Width, 2), new Color(0, 0, 0, 160));
        spriteBatch.Draw(_pixel, new Rectangle(bounds.X, bounds.Y, selected ? 8 : 3, bounds.Height), selected ? BrandRed : new Color(210, 218, 218, 64));

        DrawText(
            spriteBatch,
            text,
            bounds.X + 34,
            bounds.Y + 18,
            30f,
            selected ? 820 : 620,
            selected ? TextPrimary : TextMuted);
    }

    private void DrawPopup(SpriteBatch spriteBatch, string title, string[] items, int selectedIndex)
    {
        const int popupWidth = 720;
        const int popupHeight = 236;
        int popupX = (UiLayout.Width - popupWidth) / 2;
        int popupY = (UiLayout.Height - popupHeight) / 2;
        DrawPanel(spriteBatch, popupX, popupY, popupWidth, popupHeight, new Color(4, 6, 10, 236));
        DrawText(spriteBatch, title, popupX + 42, popupY + 34, 32f, 760, TextPrimary);
        for (int i = 0; i < items.Length; i++)
        {
            bool selected = i == selectedIndex;
            Rectangle bounds = new(popupX + 42 + i * 320, popupY + 122, 280, 70);
            DrawMainMenuItem(spriteBatch, items[i], selected, bounds);
        }
    }

    private void DrawTrackMap(SpriteBatch spriteBatch, TrackDefinition track, Rectangle bounds)
    {
        DrawPanel(spriteBatch, bounds.X - 18, bounds.Y - 18, bounds.Width + 36, bounds.Height + 36, new Color(4, 7, 10, 170));
        Vector2[] points = track.Layout switch
        {
            TrackLayout.HighSpeedRing =>
            [
                new(0.35f, 0.08f),
                new(0.92f, 0.08f),
                new(0.99f, 0.22f),
                new(0.92f, 0.37f),
                new(0.75f, 0.37f),
                new(0.62f, 0.72f),
                new(0.55f, 0.93f),
                new(0.47f, 0.72f),
                new(0.41f, 0.53f),
                new(0.33f, 0.57f),
                new(0.20f, 0.93f),
                new(0.09f, 0.47f),
                new(0.16f, 0.08f)
            ],
            _ =>
            [
                new(0.13f, 0.18f),
                new(0.30f, 0.08f),
                new(0.63f, 0.08f),
                new(0.90f, 0.11f),
                new(0.96f, 0.23f),
                new(0.88f, 0.36f),
                new(0.73f, 0.38f),
                new(0.65f, 0.53f),
                new(0.56f, 0.72f),
                new(0.47f, 0.69f),
                new(0.43f, 0.53f),
                new(0.35f, 0.55f),
                new(0.20f, 0.82f),
                new(0.10f, 0.62f),
                new(0.03f, 0.34f)
            ]
        };

        for (int i = 0; i < points.Length; i++)
        {
            Vector2 a = ToBounds(points[i], bounds);
            Vector2 b = ToBounds(points[(i + 1) % points.Length], bounds);
            DrawLine(spriteBatch, a, b, 5f, new Color(216, 224, 222, 232));
        }
    }

    private void DrawLine(SpriteBatch spriteBatch, Vector2 a, Vector2 b, float width, Color color)
    {
        Vector2 delta = b - a;
        float distance = delta.Length();
        if (distance <= 0.001f)
        {
            return;
        }

        float rotation = MathF.Atan2(delta.Y, delta.X);
        spriteBatch.Draw(
            _pixel,
            a,
            null,
            color,
            rotation,
            new Vector2(0f, 0.5f),
            new Vector2(distance, width),
            SpriteEffects.None,
            0f);
    }

    private static Vector2 ToBounds(Vector2 normalized, Rectangle bounds)
    {
        return new Vector2(
            bounds.X + normalized.X * bounds.Width,
            bounds.Y + normalized.Y * bounds.Height);
    }

    private void DrawPanel(SpriteBatch spriteBatch, int x, int y, int width, int height, Color fill)
    {
        Rectangle bounds = new(x, y, width, height);
        spriteBatch.Draw(_pixel, bounds, fill);
        spriteBatch.Draw(_pixel, new Rectangle(x, y, width, 2), PanelStroke);
        spriteBatch.Draw(_pixel, new Rectangle(x, y + height - 2, width, 2), new Color(0, 0, 0, 180));
        spriteBatch.Draw(_pixel, new Rectangle(x, y, 2, height), PanelStroke);
        spriteBatch.Draw(_pixel, new Rectangle(x + width - 2, y, 2, height), new Color(0, 0, 0, 120));
    }

    private void DrawText(SpriteBatch spriteBatch, string text, float x, float y, float size, int weight, Color color)
    {
        _fonts.Draw(spriteBatch, TachometerFontRole.Orbitron, text, new Vector2(x, y), size, weight, color);
    }

    private void DrawCentered(SpriteBatch spriteBatch, string text, Vector2 center, float size, int weight, Color color)
    {
        _fonts.DrawCentered(spriteBatch, TachometerFontRole.Orbitron, text, center, size, weight, color);
    }

    private static int NormalizeLegacyY(int y)
    {
        return y < 320 ? y * 4 : y;
    }

    private static string ResolveExistingAssetPath(string relativePath)
    {
        foreach (string path in GetCandidateAssetPaths(relativePath))
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        throw new FileNotFoundException($"Asset file not found: {relativePath}", relativePath);
    }

    private static Texture2D LoadTextureOrCreateFallback(GraphicsDevice graphicsDevice, string relativePath)
    {
        foreach (string path in GetCandidateAssetPaths(relativePath))
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                using FileStream stream = File.OpenRead(path);
                return Texture2D.FromStream(graphicsDevice, stream, DefaultColorProcessors.PremultiplyAlpha);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException or NotSupportedException)
            {
                Console.Error.WriteLine($"Could not load menu background '{path}': {exception.Message}");
            }
        }

        Texture2D fallback = new(graphicsDevice, 2, 2, false, SurfaceFormat.Color);
        fallback.SetData([
            new Color(8, 12, 18),
            new Color(10, 14, 20),
            new Color(4, 8, 12),
            new Color(7, 10, 15)
        ]);
        return fallback;
    }

    private static IEnumerable<string> GetCandidateAssetPaths(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            yield return relativePath;
            yield break;
        }

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        string currentDirectoryPath = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, relativePath));
        string outputDirectoryPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, relativePath));

        if (seen.Add(currentDirectoryPath))
        {
            yield return currentDirectoryPath;
        }

        if (seen.Add(outputDirectoryPath))
        {
            yield return outputDirectoryPath;
        }
    }

    private static string FormatTime(TimeSpan time)
    {
        return $"{(int)time.TotalMinutes:0}:{time.Seconds:00}.{time.Milliseconds / 10:00}";
    }
}
