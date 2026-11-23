#include "debug_config.h"
#include <cassert>
#include <iostream>

int main()
{
    int diagnosticEvaluations = 0;
    URUI_DEBUG_CODE(++diagnosticEvaluations;)

#if defined(URUI_DEBUG)
    assert(diagnosticEvaluations == 1);
#else
    // Disabled instrumentation must not evaluate its arguments.
    assert(diagnosticEvaluations == 0);
#endif

    std::cout << "DEBUG_GATE_CONTRACT_PASS\n";
}
