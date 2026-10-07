using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace GlamourTagger.Windows;

// Small helper window shown with the native Fitting Room: lets the user clear the plugin's per-slot try-on memory.
public class FittingRoomAssistWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    // Deliberately duplicated slot-icon data (not shared with MainWindow_EquipmentBar.cs's
    // dye/paint bar) - smaller icons, different colors, different purpose.
    private record SlotIconInfo(string SlotName, string DisplayName, float CropX, float CropY, float CropWidth, float CropHeight);

    // crop rects into ui/uld/armouryboard_hr1.tex
    private static readonly SlotIconInfo[] SlotIcons =
    {
        new("MainHand", "Main Hand",    0f,   0f, 80f, 80f),
        new("OffHand",  "Off Hand",     240f, 80f, 80f, 80f),
        new("Head",     "Head",         80f,  0f, 80f, 80f),
        new("Body",     "Chest",        160f, 0f, 80f, 80f),
        new("Hands",    "Gloves",       240f, 0f, 80f, 80f),
        new("Legs",     "Pants",        80f, 80f, 80f, 80f),
        new("Feet",     "Feet",         160f, 80f, 80f, 80f),
        new("Ears",     "Earrings",     0f, 160f, 80f, 80f),
        new("Neck",     "Necklace",     80f, 160f, 80f, 80f),
        new("Wrists",   "Bracelet",     160f, 160f, 80f, 80f),
        new("Finger",   "Ring (Right)", 240f, 160f, 80f, 80f),
    };

    private const float IconSize = 30f;
    private const float RowGap = 8f;

    // Natural width of the "ALL" + 11 slot icons group (touching, no gap between icons,
    // 1px gap after ALL) - this drives the window's minimum width.
    private static readonly float SlotBarWidth = IconSize + 1f + SlotIcons.Length * IconSize;
    private const float ClearUnprotectedMinWidth = 130f;
    private const float ActionRowNaturalWidth = 200f + 90f + 150f + RowGap * 3 + IconSize; // current button widths + gaps + trailing icon
    private static readonly float MinWindowWidth =
        MathF.Max(SlotBarWidth + 3f + ClearUnprotectedMinWidth, ActionRowNaturalWidth) + 20f; // +20 ~ window padding

    private static readonly Vector4 ProtectedColor = new(0.75f, 0.60f, 0.10f, 1.0f); // dark gold
    private static readonly Vector4 WillClearColor = new(0.55f, 0.16f, 0.16f, 1.0f); // dark red
    private static readonly Vector4 BtnRed = new(0.35f, 0.10f, 0.10f, 0.95f);
    private static readonly Vector4 BtnRedHover = new(0.45f, 0.15f, 0.15f, 0.95f);
    private static readonly Vector4 BtnRedActive = new(0.55f, 0.20f, 0.20f, 0.95f);
    private static readonly Vector4 BtnGold = new(0.45f, 0.36f, 0.08f, 0.95f);
    private static readonly Vector4 BtnGoldHover = new(0.55f, 0.44f, 0.12f, 0.95f);
    private static readonly Vector4 BtnGoldActive = new(0.65f, 0.52f, 0.16f, 0.95f);

    private ISharedImmediateTexture? armouryTexture;

    // Session-only state
    private readonly HashSet<string> protectedSlots = new(StringComparer.OrdinalIgnoreCase);
    private bool userCustomizedProtection = false;
    private bool infoHeaderInitialized = false;

    public FittingRoomAssistWindow(Plugin plugin) : base("Glamour Tagger - Fitting Room Assist", ImGuiWindowFlags.NoCollapse)
    {
        this.plugin = plugin;
        this.Size = new Vector2(MinWindowWidth, 270);
        this.SizeCondition = ImGuiCond.Appearing;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(MinWindowWidth, 120f),
            MaximumSize = new Vector2(4000f, 4000f)
        };
    }

    public void Dispose() { }

    /// Call when a brand-new Fitting Room session starts (first plugin-initiated try-on
    /// after the previous session fully closed).
    public void NotifySessionStarted()
    {
        protectedSlots.Clear();
        userCustomizedProtection = false;
        IsOpen = true;
    }

    /// Call when the native Fitting Room actually closes.
    public void NotifySessionEnded()
    {
        IsOpen = false;
        protectedSlots.Clear();
        userCustomizedProtection = false;
    }

    // on first appearance: sit right above the game's Fitting Room window
    public override void PreDraw()
    {
        base.PreDraw();

        var pos = ComputeDefaultPosition();
        if (pos.HasValue)
        {
            ImGui.SetNextWindowPos(pos.Value, ImGuiCond.Appearing);
        }
    }

    private unsafe Vector2? ComputeDefaultPosition()
    {
        var unit = Plugin.GameGui.GetAddonByName<AtkUnitBase>("FittingShop");
        if (unit == null || !unit->IsVisible) return null;

        float x = unit->X;
        float y = unit->Y;
        float windowHeight = this.Size?.Y ?? 270f;

        float targetY = y - windowHeight - 8f;
        if (targetY < 0f) targetY = 0f; // stay reachable even if that means overlapping the addon

        return new Vector2(x, targetY);
    }

    public override void Draw()
    {
        DrawInfoSection();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawActionRow();
        DrawProtectedSlotRow();

        AutoFitHeight();
    }

    /// Resizes the window to exactly fit its content every frame - height is fully
    /// dynamic, width is left alone (user-draggable, floored by SizeConstraints).
    private void AutoFitHeight()
    {
        float desiredHeight = ImGui.GetCursorPosY() + ImGui.GetStyle().WindowPadding.Y;
        if (MathF.Abs(desiredHeight - ImGui.GetWindowHeight()) > 0.5f)
        {
            ImGui.SetWindowSize(new Vector2(ImGui.GetWindowWidth(), desiredHeight));
        }
    }

    // collapsible explanation; its open/closed state is remembered in the config
    private void DrawInfoSection()
    {
        if (!infoHeaderInitialized)
        {
            ImGui.SetNextItemOpen(plugin.Configuration.FittingRoomInfoExpanded, ImGuiCond.Once);
            infoHeaderInitialized = true;
        }

        bool isOpen = ImGui.CollapsingHeader("How & why this works");
        if (isOpen != plugin.Configuration.FittingRoomInfoExpanded)
        {
            plugin.Configuration.FittingRoomInfoExpanded = isOpen;
            plugin.Configuration.Save();
        }

        if (isOpen)
        {
            ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
            ImGui.TextWrapped(
                "Glamour Tagger remembers, per equipment slot, the last item YOU tried on through " +
                "this plugin (right-click Try On, or dyeing an item while in Fitting Room mode), " +
                "till the Fitting Room is open." +
                "It uses that memory so dyeing a slot with the plugin targets the right item.");
            ImGui.TextWrapped(
               "It can't see if you toggle on your gear or the outfit button in it, nor " +
               "any class/job switch you make elsewhere - so this memory " +
               "can go stale, and dyeing a slot will then use an old item, the last previewed," +
               " instead of what's actually shown.");
            ImGui.TextWrapped("--- Use the controls below to clear all or parts of this memory. ---");
            ImGui.TextDisabled("(Memory is cleared once the fitting room closes.)");
            ImGui.PopTextWrapPos();
        }
    }

    // row 1: job-change toggle | Clear All | Clear All but Last + icon of the last tried-on slot
    private void DrawActionRow()
    {
        bool jobChangeClear = plugin.Configuration.ClearFittingRoomCacheOnJobChange;

        // Fill available width, keeping the 3 buttons' current relative proportions,
        // and always leaving room for the trailing icon.
        float avail = ImGui.GetContentRegionAvail().X;
        float buttonsTotal = MathF.Max(0f, avail - IconSize - RowGap * 3);
        const float ratioA = 200f, ratioB = 90f, ratioC = 150f, ratioSum = ratioA + ratioB + ratioC;
        float w1 = buttonsTotal * (ratioA / ratioSum);
        float w2 = buttonsTotal * (ratioB / ratioSum);
        float w3 = buttonsTotal * (ratioC / ratioSum);

        PushButtonColors(jobChangeClear);
        if (ImGui.Button(jobChangeClear ? "Clear on Job Change: ON" : "Clear on Job Change: OFF", new Vector2(w1, 26)))
        {
            plugin.Configuration.ClearFittingRoomCacheOnJobChange = !jobChangeClear;
            plugin.Configuration.Save();
        }
        PopButtonColors();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("When ON, switching class/job while the Fitting Room is open\nautomatically forgets everything tracked here.");

        ImGui.SameLine(0, RowGap);

        PushButtonColors(false);
        if (ImGui.Button("Clear All", new Vector2(w2, 26)))
        {
            plugin.ClearFittingRoomCache();
        }
        PopButtonColors();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Forgets every tracked try-on item.");

        ImGui.SameLine(0, RowGap);

        PushButtonColors(false);
        bool clearButLast = ImGui.Button("Clear All but Last:", new Vector2(w3, 26));
        PopButtonColors();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Forgets everything except the item shown here:");

        ImGui.SameLine(0, RowGap);
        DrawLastTryOnIcon();

        // drop everything except the keys of the last try-on
        if (clearButLast)
        {
            var keep = new HashSet<string>(plugin.LastFittingRoomTryOnKeys, StringComparer.OrdinalIgnoreCase);
            foreach (var key in new List<string>(plugin.FittingRoomItems.Keys))
            {
                if (!keep.Contains(key))
                    plugin.FittingRoomItems.Remove(key);
            }
        }
    }

    private void DrawLastTryOnIcon()
    {
        string? rep = GetRepresentativeSlot(plugin.LastFittingRoomTryOnKeys);
        if (rep == null)
        {
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 0.6f));
            ImGui.TextUnformatted(FontAwesomeIcon.Ban.ToIconString());
            ImGui.PopStyleColor();
            ImGui.PopFont();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("None yet");
            return;
        }

        var slot = Array.Find(SlotIcons, s => s.SlotName.Equals(rep, StringComparison.OrdinalIgnoreCase));
        if (slot == null) { ImGui.TextDisabled(rep); return; }

        armouryTexture ??= Plugin.TextureProvider.GetFromGame("ui/uld/armouryboard_hr1.tex");
        var wrap = armouryTexture.GetWrapOrDefault();
        if (wrap == null) { ImGui.TextDisabled(slot.DisplayName); return; }

        Vector2 uv0 = new(slot.CropX / wrap.Width, slot.CropY / wrap.Height);
        Vector2 uv1 = new((slot.CropX + slot.CropWidth) / wrap.Width, (slot.CropY + slot.CropHeight) / wrap.Height);

        ImGui.Image(wrap.Handle, new Vector2(IconSize, IconSize), uv0, uv1, ProtectedColor);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(slot.DisplayName);
    }

    // one try-on can register several keys (OffHand/Offhand/Shield, Finger/RFinger) - pick the one that has an icon
    private static string? GetRepresentativeSlot(IReadOnlyCollection<string> keys)
    {
        if (keys.Count == 0) return null;

        bool Has(string k)
        {
            foreach (var key in keys)
                if (key.Equals(k, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        if (Has("MainHand")) return "MainHand";
        if (Has("OffHand") || Has("Offhand") || Has("Shield")) return "OffHand";
        if (Has("RFinger") || Has("Finger")) return "Finger";

        foreach (var k in keys) return k;
        return null;
    }

    // row 2: ALL + slot icons (gold = protected, red = will be cleared) | Clear Unprotected
    private void DrawProtectedSlotRow()
    {
        // until the user clicks an icon, the protected slot simply follows the last try-on
        if (!userCustomizedProtection)
        {
            string? rep = GetRepresentativeSlot(plugin.LastFittingRoomTryOnKeys);
            protectedSlots.Clear();
            if (rep != null) protectedSlots.Add(rep);
        }

        armouryTexture ??= Plugin.TextureProvider.GetFromGame("ui/uld/armouryboard_hr1.tex");
        var wrap = armouryTexture.GetWrapOrDefault();

        ImGui.BeginGroup();

        bool allProtected = protectedSlots.Count == SlotIcons.Length;
        Vector2 allPos = ImGui.GetCursorScreenPos();
        bool allHovering = ImGui.IsMouseHoveringRect(allPos, allPos + new Vector2(IconSize, IconSize));
        Vector4 allTextColor = allProtected ? ProtectedColor : WillClearColor;
        if (allHovering) allTextColor = Lighten(allTextColor, 0.18f);

        ImGui.PushID("FRA_AllBtn");
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.Text, allTextColor);
        if (ImGui.Button("ALL", new Vector2(IconSize, IconSize)))
        {
            userCustomizedProtection = true;
            if (allProtected) protectedSlots.Clear();
            else { protectedSlots.Clear(); foreach (var s in SlotIcons) protectedSlots.Add(s.SlotName); }
        }
        ImGui.PopStyleColor(4);
        ImGui.PopID();

        ImGui.SameLine(0, 1f);

        for (int i = 0; i < SlotIcons.Length; i++)
        {
            if (i > 0) ImGui.SameLine(0, 0f);
            DrawClickableSlotIcon(SlotIcons[i], wrap);
        }

        ImGui.EndGroup();

        ImGui.SameLine(0, 3);

        float clearBtnWidth = MathF.Max(ClearUnprotectedMinWidth, ImGui.GetContentRegionAvail().X);
        PushButtonColors(false);
        if (ImGui.Button("Clear Unprotected", new Vector2(clearBtnWidth, 26)))
        {
            foreach (var key in new List<string>(plugin.FittingRoomItems.Keys))
            {
                if (!protectedSlots.Contains(CanonicalizeKey(key)))
                    plugin.FittingRoomItems.Remove(key);
            }
        }
        PopButtonColors();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Clears everything except the slots marked gold on the left.\nLeft-click a slot: protect only it. Right-click: add/remove it.");
    }

    // alias keys -> the slot names the icons use
    private static string CanonicalizeKey(string key)
    {
        if (key.Equals("Offhand", StringComparison.OrdinalIgnoreCase) || key.Equals("Shield", StringComparison.OrdinalIgnoreCase))
            return "OffHand";
        if (key.Equals("RFinger", StringComparison.OrdinalIgnoreCase))
            return "Finger";
        return key;
    }

    // left click = protect only this, rightclick = add/remove
    private void DrawClickableSlotIcon(SlotIconInfo slot, IDalamudTextureWrap? wrap)
    {
        bool isProtected = protectedSlots.Contains(slot.SlotName);
        Vector4 baseTint = isProtected ? ProtectedColor : WillClearColor;

        ImGui.PushID($"FRA_Slot_{slot.SlotName}");
        if (wrap != null)
        {
            Vector2 cursorPos = ImGui.GetCursorScreenPos();
            bool hovering = ImGui.IsMouseHoveringRect(cursorPos, cursorPos + new Vector2(IconSize, IconSize));
            Vector4 tint = hovering ? Lighten(baseTint, 0.18f) : baseTint;

            Vector2 uv0 = new(slot.CropX / wrap.Width, slot.CropY / wrap.Height);
            Vector2 uv1 = new((slot.CropX + slot.CropWidth) / wrap.Width, (slot.CropY + slot.CropHeight) / wrap.Height);

            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(1, 1));

            ImGui.ImageButton(wrap.Handle, new Vector2(IconSize, IconSize), uv0, uv1, 0, new Vector4(0, 0, 0, 0), tint);

            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                userCustomizedProtection = true;
                protectedSlots.Clear();
                protectedSlots.Add(slot.SlotName);
            }
            else if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                userCustomizedProtection = true;
                if (protectedSlots.Contains(slot.SlotName)) protectedSlots.Remove(slot.SlotName);
                else protectedSlots.Add(slot.SlotName);
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"{slot.DisplayName}\nLeft-click: protect only this\nRight-click: add/remove from protected");

            ImGui.PopStyleVar();
            ImGui.PopStyleColor(3);
        }
        // texture not loaded yet -> plain text button
        else if (ImGui.Button(slot.DisplayName, new Vector2(IconSize, IconSize)))
        {
            userCustomizedProtection = true;
            protectedSlots.Clear();
            protectedSlots.Add(slot.SlotName);
        }
        ImGui.PopID();
    }

    private static Vector4 Lighten(Vector4 c, float amount)
    {
        return new Vector4(
            MathF.Min(1f, c.X + amount),
            MathF.Min(1f, c.Y + amount),
            MathF.Min(1f, c.Z + amount),
            c.W);
    }

    // gold = the toggle is on, red = normal action button
    private static void PushButtonColors(bool gold)
    {
        if (gold)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, BtnGold);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, BtnGoldHover);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, BtnGoldActive);
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Button, BtnRed);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, BtnRedHover);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, BtnRedActive);
        }
    }

    private static void PopButtonColors() => ImGui.PopStyleColor(3);
}
