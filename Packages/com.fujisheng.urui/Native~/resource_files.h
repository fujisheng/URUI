#pragma once
#include <URUI/Core/FileInterface.h>
#include <algorithm>
#include <cstring>
#include <memory>
#include <unordered_map>
#include <vector>

// 文档虚拟文件承载 HTML link 和字体等资源；未注册文件交给平台接口。
// 不支持 CSS @import。
class ResourceFiles final : public URUI::FileInterface
{
public:
    using Data = std::shared_ptr<const std::vector<uint8_t>>;

private:
    struct OpenFile
    {
        Data bytes;
        URUI::FileHandle original = 0;
        size_t position = 0;
    };
    static URUI::String LocalFilePath(URUI::String value)
    {
        if (value.compare(0, 7, "file://") == 0) {
            value.erase(0, 7);
            if (!value.empty() && value[0] != '/') value = "//" + value;
#if defined(_WIN32)
            if (value.size() > 3 && value[0] == '/' && value[2] == ':') value.erase(0, 1);
#endif
        } else if (value.find("://") != URUI::String::npos) return value;
        const auto suffix = value.find_first_of("?#");
        if (suffix != URUI::String::npos) value.resize(suffix);
        auto hex = [](char ch) { if (ch >= '0' && ch <= '9') return ch - '0'; if (ch >= 'a' && ch <= 'f') return ch - 'a' + 10; if (ch >= 'A' && ch <= 'F') return ch - 'A' + 10; return -1; };
        URUI::String decoded;
        for (size_t index = 0; index < value.size(); ++index) {
            if (value[index] == '%' && index + 2 < value.size()) {
                const int high = hex(value[index+1]), low = hex(value[index+2]);
                if (high >= 0 && low >= 0) {
                    const char byte = char(high * 16 + low);
                    if (!byte) return {};
                    decoded += byte; index += 2; continue;
                }
            }
            decoded += value[index];
        }
        return decoded;
    }
    URUI::FileInterface* fallback;
    std::unordered_map<URUI::String, Data> resources;
    std::unordered_map<URUI::FileHandle, OpenFile> open_files;
    URUI::FileHandle next = 1;

public:
    explicit ResourceFiles(URUI::FileInterface* original) : fallback(original) {}
    ~ResourceFiles() override
    {
        for (const auto& pair : open_files)
            if (pair.second.original) fallback->Close(pair.second.original);
    }
    URUI::FileInterface* Original() const { return fallback; }
    bool Register(const char* source, const uint8_t* bytes, int length)
    {
        if (!source || !*source || length < 0 || (!bytes && length) || resources.count(source))
            return false;
        auto copy = std::make_shared<std::vector<uint8_t>>();
        if (length) copy->assign(bytes, bytes + length);
        return resources.emplace(source, std::move(copy)).second;
    }
    void SetShared(const URUI::String& source, Data bytes) { resources.insert_or_assign(source, std::move(bytes)); }
    URUI::FileHandle Open(const URUI::String& path) override
    {
        if (!next) return 0;
        OpenFile file;
        const auto resource = resources.find(path);
        if (resource != resources.end()) file.bytes = resource->second;
        else
        {
            file.original = fallback->Open(LocalFilePath(path));
            if (!file.original) return 0;
        }
        const auto handle = next++;
        try { open_files.emplace(handle, file); }
        catch (...)
        {
            if (file.original) fallback->Close(file.original);
            throw;
        }
        return handle;
    }
    void Close(URUI::FileHandle file) override
    {
        const auto found = open_files.find(file);
        if (found == open_files.end()) return;
        if (found->second.original) fallback->Close(found->second.original);
        open_files.erase(found);
    }
    size_t Read(void* buffer, size_t size, URUI::FileHandle file) override
    {
        const auto found = open_files.find(file);
        if (found == open_files.end() || (!buffer && size)) return 0;
        auto& value = found->second;
        if (value.original) return fallback->Read(buffer, size, value.original);
        const auto count = std::min(size, value.bytes->size() - value.position);
        if (count) std::memcpy(buffer, value.bytes->data() + value.position, count);
        value.position += count;
        return count;
    }
    bool Seek(URUI::FileHandle file, long offset, int origin) override
    {
        const auto found = open_files.find(file);
        if (found == open_files.end()) return false;
        auto& value = found->second;
        if (value.original) return fallback->Seek(value.original, offset, origin);
        if (origin != SEEK_SET && origin != SEEK_CUR && origin != SEEK_END) return false;
        const auto base = origin == SEEK_SET ? int64_t(0) :
            origin == SEEK_CUR ? int64_t(value.position) : int64_t(value.bytes->size());
        if (offset < 0 ? int64_t(offset) < -base : uint64_t(offset) > value.bytes->size() - size_t(base))
            return false;
        const auto position = base + int64_t(offset);
        value.position = size_t(position);
        return true;
    }
    size_t Tell(URUI::FileHandle file) override
    {
        const auto found = open_files.find(file);
        if (found == open_files.end()) return 0;
        return found->second.original ? fallback->Tell(found->second.original) : found->second.position;
    }
    size_t Length(URUI::FileHandle file) override
    {
        const auto found = open_files.find(file);
        if (found == open_files.end()) return 0;
        return found->second.original ? fallback->Length(found->second.original) : found->second.bytes->size();
    }
};
