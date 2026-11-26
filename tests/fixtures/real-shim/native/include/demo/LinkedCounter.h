#pragma once

#include <functional>

namespace demo
{
class LinkedCounter
{
public:
    explicit LinkedCounter(int value);
    ~LinkedCounter();

    [[nodiscard]] int value() const;

    [[nodiscard]] int invoke(std::function<int(int)> const& callback, int value) const;

private:
    int value_;
};
} // namespace demo
