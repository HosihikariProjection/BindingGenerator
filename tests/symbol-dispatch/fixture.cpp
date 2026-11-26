// Native fixture for the symbol dispatch smoke test.
//
// The DLL is built with a PDB so the symbol frontend can recover RVAs and vtable
// slots without any headers. Only the exported behaviour is stable; the compiler
// is free to lay out the class as it sees fit.

#include <memory>
#include <optional>
#include <string>
#include <utility>
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

namespace gsl
{
template <typename T>
struct span
{
    T*          data;
    std::size_t size;
};
} // namespace gsl

static FixtureObject  g_spanValues[2]  = {{11}, {22}};
static FixtureObject* g_spanObjects[2] = {&g_spanValues[0], &g_spanValues[1]};

template <typename T>
struct OwnerPtr
{
    std::shared_ptr<T> mHandle;
    OwnerPtr() = default;
    explicit OwnerPtr(std::shared_ptr<T> value)
        : mHandle(std::move(value))
    {
    }
};

template <typename T>
struct StackRefResult
{
    std::shared_ptr<T> mHandle;
    StackRefResult() = default;
    explicit StackRefResult(std::shared_ptr<T> value)
        : mHandle(std::move(value))
    {
    }
};

template <typename T>
struct WeakRef
{
    std::weak_ptr<T> mHandle;
    WeakRef() = default;
    explicit WeakRef(std::weak_ptr<T> value)
        : mHandle(std::move(value))
    {
    }
};

namespace gsl
{
template <typename T>
struct not_null
{
    T value;
    explicit not_null(T inner)
        : value(std::move(inner))
    {
    }
};
} // namespace gsl

struct FixtureHeavy
{
    std::string text;

    explicit FixtureHeavy(std::string value);
    FixtureHeavy(FixtureHeavy const& other);
    ~FixtureHeavy();
};

FixtureHeavy::FixtureHeavy(std::string value)
    : text(std::move(value))
{
}

FixtureHeavy::FixtureHeavy(FixtureHeavy const& other)
    : text(other.text)
{
}

FixtureHeavy::~FixtureHeavy() = default;

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
    int                                    add(int value) const;
    virtual int                            isReady() const;
    virtual int                            scale(int factor) const;
    std::string                            label() const;
    std::vector<int>                       numbers() const;
    std::vector<std::string>               names() const;
    static std::vector<int>                squares(int count);
    int                                    countNames(std::vector<std::string> const& values) const;
    int                                    sumValues(std::vector<int> const& values) const;
    FixturePair                            pair() const;
    int                                    sumPair(FixturePair value) const;
    FixtureHeavy                           heavy(int value) const;
    std::unique_ptr<FixtureHeavy>          uniqueHeavy(int value) const;
    int                                    heavySize(FixtureHeavy const& value) const;
    std::optional<int>                     maybeNumber(bool present) const;
    std::optional<FixtureValue>            maybeValue(bool present) const;
    int                                    sumValueOptional(std::optional<FixtureValue> value) const;
    int                                    sumOptional(std::optional<int> value) const;
    std::optional<std::string>             maybeLabel(bool present) const;
    int                                    labelSize(std::optional<std::string> value) const;
    std::shared_ptr<FixtureObject>         makeShared(int value) const;
    int                                    sharedValue(std::shared_ptr<FixtureObject> value) const;
    std::weak_ptr<FixtureObject>           weakRef() const;
    OwnerPtr<FixtureObject>                ownerPtr(int value) const;
    StackRefResult<FixtureObject>          stackRef(int value) const;
    WeakRef<FixtureObject>                 weakRefValue(int value) const;
    gsl::not_null<OwnerPtr<FixtureObject>> notNullOwner(int value) const;
    FixtureObject*                         makeObject(int value) const;
    void                                   releaseObject(FixtureObject* value) const;
    int                                    sumObjectValues(std::vector<FixtureObject*> const& values) const;
    gsl::span<FixtureObject*>              objectSpan() const;
    static FixtureBlock*                   create(int x, int y);
    static void                            release(FixtureBlock* block);

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

std::vector<std::string> FixtureBlock::names() const
{
    return {std::to_string(x_), std::to_string(y_)};
}

std::vector<int> FixtureBlock::squares(int count)
{
    std::vector<int> result;
    for (int i = 0; i < count; ++i)
    {
        result.push_back(i * i);
    }

    return result;
}

int FixtureBlock::countNames(std::vector<std::string> const& values) const
{
    return static_cast<int>(values.size()) + x_;
}

int FixtureBlock::sumValues(std::vector<int> const& values) const
{
    int total = 0;
    for (int value : values)
    {
        total += value;
    }

    return total + y_;
}

FixturePair FixtureBlock::pair() const
{
    return FixturePair(x_, y_);
}

FixtureHeavy FixtureBlock::heavy(int value) const
{
    return FixtureHeavy(std::to_string(value));
}

std::unique_ptr<FixtureHeavy> FixtureBlock::uniqueHeavy(int value) const
{
    return std::make_unique<FixtureHeavy>(std::to_string(value));
}

int FixtureBlock::heavySize(FixtureHeavy const& value) const
{
    return static_cast<int>(value.text.size());
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

OwnerPtr<FixtureObject> FixtureBlock::ownerPtr(int value) const
{
    g_shared = std::make_shared<FixtureObject>(FixtureObject{value});
    return OwnerPtr<FixtureObject>(g_shared);
}

StackRefResult<FixtureObject> FixtureBlock::stackRef(int value) const
{
    g_shared = std::make_shared<FixtureObject>(FixtureObject{value});
    return StackRefResult<FixtureObject>(g_shared);
}

WeakRef<FixtureObject> FixtureBlock::weakRefValue(int value) const
{
    g_shared = std::make_shared<FixtureObject>(FixtureObject{value});
    return WeakRef<FixtureObject>(std::weak_ptr<FixtureObject>(g_shared));
}

gsl::not_null<OwnerPtr<FixtureObject>> FixtureBlock::notNullOwner(int value) const
{
    g_shared = std::make_shared<FixtureObject>(FixtureObject{value});
    return gsl::not_null<OwnerPtr<FixtureObject>>(OwnerPtr<FixtureObject>(g_shared));
}

FixtureObject* FixtureBlock::makeObject(int value) const
{
    return new FixtureObject{value};
}

void FixtureBlock::releaseObject(FixtureObject* value) const
{
    delete value;
}

gsl::span<FixtureObject*> FixtureBlock::objectSpan() const
{
    return gsl::span<FixtureObject*>{g_spanObjects, 2};
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
