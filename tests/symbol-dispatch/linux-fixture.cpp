// Linux fixture for the symbol dispatch test.
//
// The shared object exports the mangled Itanium names used by the IR fixture, so the
// dispatch shim can resolve them through dlsym when no link-time address is present.

#include <memory>
#include <optional>
#include <string>
#include <vector>

struct FixtureValue
{
    int a;
    int b;
};

struct FixtureObject
{
    int value;
};

static std::shared_ptr<FixtureObject> g_shared;

struct FixturePair
{
    int x;
    int y;

    FixturePair(int x_, int y_);
    FixturePair(FixturePair const& other);
};

FixturePair::FixturePair(int x_, int y_)
    : x(x_)
    , y(y_)
{
}

FixturePair::FixturePair(FixturePair const& other)
    : x(other.x)
    , y(other.y)
{
}

class FixtureBlock
{
public:
    FixtureBlock(int x, int y);
    int                            add(int value) const;
    virtual int                    isReady() const;
    virtual int                    scale(int factor) const;
    std::string                    label() const;
    std::vector<int>               numbers() const;
    FixturePair                    pair() const;
    int                            sumPair(FixturePair value) const;
    std::optional<int>             maybeNumber(bool present) const;
    std::optional<FixtureValue>    maybeValue(bool present) const;
    int                            sumValueOptional(std::optional<FixtureValue> value) const;
    int                            sumOptional(std::optional<int> value) const;
    std::optional<std::string>     maybeLabel(bool present) const;
    int                            labelSize(std::optional<std::string> value) const;
    std::shared_ptr<FixtureObject> makeShared(int value) const;
    int                            sharedValue(std::shared_ptr<FixtureObject> value) const;
    std::weak_ptr<FixtureObject>   weakRef() const;
    FixtureObject*                 makeObject(int value) const;
    void                           releaseObject(FixtureObject* value) const;
    int                            sumObjectValues(std::vector<FixtureObject*> const& values) const;
    static FixtureBlock*           create(int x, int y);
    static void                    release(FixtureBlock* block);

private:
    int x_;
    int y_;
};

FixtureBlock::FixtureBlock(int x, int y)
    : x_(x)
    , y_(y)
{
}

int FixtureBlock::add(int value) const
{
    return x_ + y_ + value;
}

int FixtureBlock::isReady() const
{
    return 1;
}

int FixtureBlock::scale(int factor) const
{
    return (x_ + y_) * factor;
}

std::string FixtureBlock::label() const
{
    return std::to_string(x_ + y_);
}

std::vector<int> FixtureBlock::numbers() const
{
    return {x_, y_};
}

FixturePair FixtureBlock::pair() const
{
    return FixturePair(x_, y_);
}

int FixtureBlock::sumPair(FixturePair value) const
{
    return value.x + value.y + x_;
}

std::optional<int> FixtureBlock::maybeNumber(bool present) const
{
    return present ? std::optional<int>(x_) : std::nullopt;
}

int FixtureBlock::sumOptional(std::optional<int> value) const
{
    return value.value_or(0) + x_;
}

std::optional<FixtureValue> FixtureBlock::maybeValue(bool present) const
{
    return present ? std::optional<FixtureValue>(FixtureValue{x_, y_}) : std::nullopt;
}

int FixtureBlock::sumValueOptional(std::optional<FixtureValue> value) const
{
    return value ? value->a + value->b + x_ : x_;
}

std::optional<std::string> FixtureBlock::maybeLabel(bool present) const
{
    return present ? std::optional<std::string>(std::to_string(x_)) : std::nullopt;
}

int FixtureBlock::labelSize(std::optional<std::string> value) const
{
    return static_cast<int>(value ? value->size() : 0) + x_;
}

std::shared_ptr<FixtureObject> FixtureBlock::makeShared(int value) const
{
    g_shared = std::make_shared<FixtureObject>(FixtureObject{value});
    return g_shared;
}

int FixtureBlock::sharedValue(std::shared_ptr<FixtureObject> value) const
{
    return value ? value->value + x_ : -1;
}

std::weak_ptr<FixtureObject> FixtureBlock::weakRef() const
{
    return g_shared;
}

FixtureObject* FixtureBlock::makeObject(int value) const
{
    return new FixtureObject{value};
}

void FixtureBlock::releaseObject(FixtureObject* value) const
{
    delete value;
}

int FixtureBlock::sumObjectValues(std::vector<FixtureObject*> const& values) const
{
    int total = x_;
    for (FixtureObject* value : values)
    {
        total += value->value;
    }

    return total;
}

FixtureBlock* FixtureBlock::create(int x, int y)
{
    return new FixtureBlock(x, y);
}

void FixtureBlock::release(FixtureBlock* block)
{
    delete block;
}
