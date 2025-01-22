using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace RetroRoulette
{
    class StyleContext : IDisposable
    {
        private int numStyleColorsPushed = 0;
        private int numStyleVarsPushed = 0;
        private bool pushedFont = false;
        private bool beganDisabled = false;

        public void SetDisabled(bool disabled)
        {
            Debug.Assert(!beganDisabled);

            ImGui.BeginDisabled(disabled);
            beganDisabled = true;
        }

        public void SetStyleColor(ImGuiCol idx, uint col)
        {
            ImGui.PushStyleColor(idx, col);
            numStyleColorsPushed++;
        }

        public void SetStyleColor(ImGuiCol idx, Vector4 col)
        {
            ImGui.PushStyleColor(idx, col);
            numStyleColorsPushed++;
        }

        public void SetStyleVar(ImGuiStyleVar idx, float val)
        {
            ImGui.PushStyleVar(idx, val);
            numStyleVarsPushed++;
        }

        public void SetStyleVar(ImGuiStyleVar idx, Vector2 val)
        {
            ImGui.PushStyleVar(idx, val);
            numStyleVarsPushed++;
        }

        public void SetFont(ImFontPtr font)
        {
            Debug.Assert(!pushedFont);

            ImGui.PushFont(font);
            pushedFont = true;
        }

        public void Dispose()
        {
            if (numStyleColorsPushed > 0)
                ImGui.PopStyleColor(numStyleColorsPushed);

            if (numStyleVarsPushed > 0)
                ImGui.PopStyleVar(numStyleVarsPushed);

            if (pushedFont)
                ImGui.PopFont();

            if (beganDisabled)
                ImGui.EndDisabled();
        }
    }
}
