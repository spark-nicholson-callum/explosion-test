using System;

public class DoubleBuffer<T>
{
    private T[] buffers = new T[2];
    private int readIndex = 0;
    private int writeIndex = 1;

    public T ReadBuffer => buffers[readIndex];
    public T WriteBuffer => buffers[writeIndex];

    public DoubleBuffer(T buffer1, T buffer2)
    {
        buffers[readIndex] = buffer1;
        buffers[writeIndex] = buffer2;
    } 

    public void ForEach(Action<T> action) {
        action(ReadBuffer);
        action(WriteBuffer);
    }

    public void SwapBuffers()
    {
        (readIndex, writeIndex) = (writeIndex, readIndex);
    }
}
