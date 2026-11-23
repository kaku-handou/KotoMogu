using System;

namespace KotoMogu.InputPreview
{
    // No dependency on Unity, the model or an Animator.
    public static class PreviewPoseLogic
    {
        public static readonly string[] Labels = { "open_palm", "fist", "thumb_up", "point", "finger_gun", "victory", "rock", "shaka" };

        public static string Classify(int thumb, int index, int middle, int ring, int pinky,
            float grab, float tx, float ty, float tz)
        {
            if (float.IsNaN(grab) || float.IsInfinity(grab) || grab < 0 || grab > 1) return "";
            int[] fingers = { thumb, index, middle, ring, pinky };
            foreach (int f in fingers) if (f != 0 && f != 1) return "";
            if (thumb == 0 && index == 1 && middle == 1 && ring == 0 && pinky == 0) return "victory";
            if (thumb == 0 && index == 1 && middle == 0 && ring == 0 && pinky == 1) return "rock";
            if (thumb == 1 && index == 0 && middle == 0 && ring == 0 && pinky == 1) return "shaka";
            bool threeClosed = middle == 0 && ring == 0 && pinky == 0;
            if (index == 1 && threeClosed) return thumb == 1 ? "finger_gun" : "point";
            double length = Math.Sqrt((double)tx * tx + (double)ty * ty + (double)tz * tz);
            if (double.IsNaN(length) || double.IsInfinity(length)) return "";
            if (thumb == 1 && index == 0 && threeClosed && length > 0.0001 && ty / length >= 0.5) return "thumb_up";
            if (thumb == 0 && index == 0 && threeClosed && grab >= 0.8f) return "fist";
            if (thumb == 1 && index == 1 && middle == 1 && ring == 1 && pinky == 1 && grab <= 0.3f) return "open_palm";
            return "";
        }

        public static string GestureName(string label)
        {
            switch (label)
            {
                case "open_palm": return "张开手掌";
                case "fist": return "握拳";
                case "thumb_up": return "竖大拇指";
                case "point": return "伸食指";
                case "finger_gun": return "L 形";
                case "victory": return "V 形";
                case "rock": return "食指与小指";
                case "shaka": return "拇指与小指";
                default: return label;
            }
        }

        public static string ActionName(string label)
        {
            switch (label)
            {
                case "open_palm": return "打招呼 / Greet";
                case "fist": return "吃东西 / Eat";
                case "thumb_up": return "开心 / Happy";
                case "point": return "轻咬 / Bite";
                case "finger_gun": return "装死 / PlayDead";
                case "victory": return "胜利欢呼 / Victory";
                case "rock": return "奶凶吼叫 / Roar";
                case "shaka": return "害羞捂脸 / Shy";
                default: return "未识别";
            }
        }

        // A held pose fires once. A deliberate neutral interval rearms the same pose.
        // Switching to a different pose may fire after its own stable interval.
        public sealed class Gate
        {
            int id;
            bool hasId;
            string candidate = "", latched = "";
            float since, neutralSince = -1;
            public void Reset() { hasId = false; candidate = latched = ""; neutralSince = -1; }
            public bool Observe(int handId, string label, float now, float stable, float release)
            {
                if (!hasId || id != handId) { Reset(); id = handId; hasId = true; }
                if (string.IsNullOrEmpty(label))
                {
                    candidate = "";
                    if (neutralSince < 0) neutralSince = now;
                    if (now - neutralSince >= release) latched = "";
                    return false;
                }
                neutralSince = -1;
                if (candidate != label) { candidate = label; since = now; }
                if (latched == label || now - since < stable) return false;
                latched = label;
                return true;
            }
        }
    }
}
