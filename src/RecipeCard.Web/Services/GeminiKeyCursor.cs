namespace RecipeCard.Web.Services;

public class GeminiKeyCursor
{
    private int _counter = -1;

    public int NextStart(int count)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Số lượng key phải lớn hơn 0.");
        }

        var next = Interlocked.Increment(ref _counter);
        return (int)((uint)next % (uint)count);
    }
}
