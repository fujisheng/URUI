#include "StreamFile.h"
#include "../../Include/URUI/Core/Core.h"
#include "../../Include/URUI/Core/FileInterface.h"
#include "../../Include/URUI/Core/StringUtilities.h"

namespace URUI
{

    StreamFile::StreamFile(FileInterface* requested_interface)
        : file_interface(requested_interface ? requested_interface : GetFileInterface())
    {
        file_handle = 0;
        length = 0;
    }

    StreamFile::~StreamFile()
    {
        if (file_handle)
        {
            StreamFile::Close();
        }
    }

    bool StreamFile::Open(const String& path)
    {
        String url_safe_path = path;
        SetStreamDetails(URL(url_safe_path), Stream::MODE_READ);

        if (file_handle)
        {
            Close();
        }

        // Fix the path if a leading colon has been replaced with a pipe.
        String fixed_path = StringUtilities::Replace(path, '|', ':');
        file_handle = file_interface->Open(fixed_path);
        if (!file_handle)
        {
            Log::Message(Log::LT_WARNING, "Unable to open file %s.", fixed_path.c_str());
            return false;
        }

        GetLength();

        return true;
    }

    void StreamFile::Close()
    {
        if (file_handle)
        {
            file_interface->Close(file_handle);
            file_handle = 0;
        }

        length = 0;
        Stream::Close();
    }

    size_t StreamFile::Length() const
    {
        return length;
    }

    size_t StreamFile::Tell() const
    {
        return file_interface->Tell(file_handle);
    }

    bool StreamFile::Seek(long offset, int origin) const
    {
        return file_interface->Seek(file_handle, offset, origin);
    }

    size_t StreamFile::Read(void* buffer, size_t bytes) const
    {
        return file_interface->Read(buffer, bytes, file_handle);
    }

    size_t StreamFile::Write(const void* /*buffer*/, size_t /*bytes*/)
    {
        URUI_ERROR;
        return 0;
    }

    size_t StreamFile::Truncate(size_t /*bytes*/)
    {
        URUI_ERROR;
        return 0;
    }

    bool StreamFile::IsReadReady()
    {
        return Tell() < Length();
    }

    bool StreamFile::IsWriteReady()
    {
        return false;
    }
    void StreamFile::GetLength()
    {
        length = file_interface->Length(file_handle);
    }

}
