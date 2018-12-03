namespace SenderModule
{
    using System;
    class Sensor
    {
        public int Value { get; set; }

        private Random rnd = new Random();

        public int GetValue()
        {
            int i = rnd.Next(0, 100);
            return i;
        }
    }
}