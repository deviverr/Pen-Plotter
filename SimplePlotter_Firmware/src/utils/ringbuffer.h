// ringbuffer.h - Circular buffer template
// SimplePlotter Firmware v1.0

#ifndef RINGBUFFER_H
#define RINGBUFFER_H

#include <stdint.h>

template<typename T, int N>
class RingBuffer {
    T buffer[N];
    int head = 0, tail = 0, count = 0;
public:
    bool push(const T& item) {
        if (count >= N) return false;
        buffer[tail] = item;
        tail = (tail + 1) % N;
        count++;
        return true;
    }
    bool pop(T& item) {
        if (count == 0) return false;
        item = buffer[head];
        head = (head + 1) % N;
        count--;
        return true;
    }
    bool isFull() const { return count >= N; }
    bool isEmpty() const { return count == 0; }
    int size() const { return count; }
};

#endif // RINGBUFFER_H
