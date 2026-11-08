using System;

namespace RadioLogic
{
    public static class RadioPolicy
    {
        public static float Range(int level) => level == 1 ? 25 : level == 2 ? 50 : level == 3 ? 125 : level == 4 ? 500 : level == 5 ? 2000 : 0;
        public static bool Reach(int a, int b, double distance) => !double.IsNaN(distance) && distance >= 0 && Range(a) > 0 && Range(b) > 0 && distance <= Math.Min(Range(a), Range(b));
        public static bool IsBinding(bool sourceTransmitter,bool sourceAntenna,bool targetReceiver,bool targetAntenna)
            => (sourceTransmitter && targetAntenna) || (sourceAntenna && targetReceiver);
        public static bool CanPair(bool firstTransmitter,bool secondTransmitter,bool separateGrids,bool friendly,bool occupied)
            => firstTransmitter!=secondTransmitter && separateGrids && friendly && !occupied;
    }
}
