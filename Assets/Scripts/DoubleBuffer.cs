public class DoubleBuffer<T>
{
    private T[] buffers = new T[2];
    private int readIndex = 0;
    private int writeIndex = 1;

    public T ReadBuffer => buffers[readIndex];
    public T WriteBuffer => buffers[writeIndex];

    public void SwapBuffers()
    {
        (readIndex, writeIndex) = (writeIndex, readIndex);
    }
}
