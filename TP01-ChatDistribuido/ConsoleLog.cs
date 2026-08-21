namespace ChatDistribuido
{
    public static class ConsoleLog
    {
        private static readonly object Gate = new();

        public static void WriteLine(string text)
        {
            lock (Gate)
            {
                Console.WriteLine(text);
            }
        }
    }
}
