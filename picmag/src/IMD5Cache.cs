namespace picmag
{
    public interface IMD5Cache
    {
        bool TryGetValue(string key, out string value);
        bool TryAdd(string key, string value);
    }
}