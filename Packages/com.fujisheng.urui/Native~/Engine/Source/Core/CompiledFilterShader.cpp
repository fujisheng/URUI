#include "../../Include/URUI/Core/CompiledFilterShader.h"
#include "RenderManagerAccess.h"

namespace URUI {

void CompiledFilter::AddHandleTo(FilterHandleList& list)
{
	if (resource_handle != InvalidHandle())
	{
		list.push_back(resource_handle);
	}
}

void CompiledFilter::Release()
{
	if (resource_handle != InvalidHandle())
	{
		RenderManagerAccess::ReleaseResource(render_manager, *this);
		Clear();
	}
}

void CompiledShader::Release()
{
	if (resource_handle != InvalidHandle())
	{
		RenderManagerAccess::ReleaseResource(render_manager, *this);
		Clear();
	}
}

} // namespace URUI
