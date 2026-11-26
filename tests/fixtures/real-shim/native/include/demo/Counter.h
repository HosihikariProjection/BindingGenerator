#pragma once

namespace demo
{
class Counter
{
public:
    explicit Counter(int value)
        : value(value)
    {
    }
    ~Counter() = default;

    [[nodiscard]] int get() const
    {
        return value;
    }

    int value;
};
} // namespace demo
