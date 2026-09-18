# GridWise Operator Directive System Prompt

## Overview
The file `interpreter-prompt.txt` serves as the system instruction for the LLM operator directive interpreter (`LlmService`).

## Runtime Lifecycle & Loading
- **Load Timing**: This system prompt file is read from disk once during the construction of `LlmService`.
- **Hot-Reloading**: Changes made to `interpreter-prompt.txt` require an application restart (or container recycling) to take effect in `LlmService`.

## Prompt Engineering Contract
The system prompt is the primary mechanism that guides and "trains" the LLM's zero-shot classification at runtime. To ensure deterministic parsing that complies with downstream guardrails, the prompt must always define:

1. **Six Canonical Directive Types**:
   - `solar_reduction`: Unexpected cloud cover or array derating.
   - `minimum_battery_reserve`: Reserve energy threshold for emergencies or critical events.
   - `no_charge_window`: Forbidden battery charging hours.
   - `no_discharge_window`: Forbidden battery discharging hours.
   - `max_grid_window`: Capped grid power draw limit.
   - `no_op`: Informational or non-actionable operator text.

2. **Half-Open Time Window Convention `[start, end)`**:
   - Operator notes specifying "from X to Y" (e.g., 1 PM to 3 PM) must be mapped to half-open intervals (`[13, 14]`), where the ending hour is excluded.
   - Hours must be strictly ascending, distinct integers in the `0..23` range.

3. **Factor as Remaining Fraction**:
   - Solar multipliers must represent the remaining fractional capacity (`0.0` to `1.0`), not the reduction percentage. For example, "drop by 30%" translates to a factor of `0.70`.

4. **Applies Semantics**:
   - Actionable directives must output `"applies": true`.
   - `no_op` directives must output `"applies": false` and `"structured_adjustment": null`.

5. **Strict JSON Output**:
   - The LLM must return pure JSON conforming to `LlmDirectiveRaw` schema without markdown wraps or explanatory preambles.
