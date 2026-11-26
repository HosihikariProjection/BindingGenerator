#include "include/demo/LinkedCounter.h"

namespace demo
{
LinkedCounter::LinkedCounter(int value)
    : value_(value)
{
}
LinkedCounter::~LinkedCounter() = default;
int LinkedCounter::value() const
{
    return value_;
}
int LinkedCounter::invoke(std::function<int(int)> const& callback, int value) const
{
    return callback(value + value_);
}
} // namespace demo
