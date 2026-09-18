document.addEventListener('DOMContentLoaded', () => {
    const loadSampleBtn = document.getElementById('loadSampleBtn');
    const runOptimizeBtn = document.getElementById('runOptimizeBtn');
    const errorBanner = document.getElementById('errorBanner');
    const errorMessage = document.getElementById('errorMessage');
    const loadingSpinner = document.getElementById('loadingSpinner');
    const resultsPlaceholder = document.getElementById('resultsPlaceholder');
    const resultsContent = document.getElementById('resultsContent');

    const scenarioIdInput = document.getElementById('scenarioIdInput');
    const notesInput = document.getElementById('notesInput');
    const batteryInput = document.getElementById('batteryInput');
    const hoursInput = document.getElementById('hoursInput');

    const summaryGridKwh = document.getElementById('summaryGridKwh');
    const summaryCostBdt = document.getElementById('summaryCostBdt');
    const summaryPeakKwh = document.getElementById('summaryPeakKwh');
    const summaryText = document.getElementById('summaryText');
    const directivesContainer = document.getElementById('directivesContainer');
    const planTableBody = document.getElementById('planTableBody');

    // Load Sample Button
    loadSampleBtn.addEventListener('click', async () => {
        hideError();
        try {
            const res = await fetch('/samples/scenario');
            if (!res.ok) throw new Error('Failed to load sample scenario.');
            const sample = await res.json();

            if (sample.scenario_id) scenarioIdInput.value = sample.scenario_id;
            if (sample.operator_notes) notesInput.value = sample.operator_notes.join('\n');
            if (sample.battery) batteryInput.value = JSON.stringify(sample.battery, null, 2);
            if (sample.hours) hoursInput.value = JSON.stringify(sample.hours, null, 2);
        } catch (err) {
            showError(`Error loading sample: ${err.message}`);
        }
    });

    // Run Optimization Button
    runOptimizeBtn.addEventListener('click', async () => {
        hideError();

        // 1. Parse Inputs
        const scenarioId = scenarioIdInput.value.trim() || 'default-scenario';
        const rawNotes = notesInput.value.split('\n').map(n => n.trim()).filter(n => n.length > 0);

        if (rawNotes.length < 1 || rawNotes.length > 3) {
            showError('Please provide between 1 and 3 operator notes (one per line).');
            return;
        }

        let batteryObj;
        try {
            batteryObj = JSON.parse(batteryInput.value);
        } catch (e) {
            showError('Invalid JSON in Battery Parameters.');
            return;
        }

        let hoursArr;
        try {
            hoursArr = JSON.parse(hoursInput.value);
            if (!Array.isArray(hoursArr) || hoursArr.length !== 24) {
                showError('24-Hour Load Profile must contain an array with exactly 24 entries.');
                return;
            }
        } catch (e) {
            showError('Invalid JSON in 24-Hour Load Profile.');
            return;
        }

        const requestBody = {
            scenario_id: scenarioId,
            operator_notes: rawNotes,
            battery: batteryObj,
            hours: hoursArr
        };

        // 2. Show Loading
        loadingSpinner.style.display = 'flex';
        resultsPlaceholder.style.display = 'none';
        resultsContent.style.display = 'none';
        runOptimizeBtn.disabled = true;

        // 3. Call POST /optimize-energy/raw
        try {
            const res = await fetch('/optimize-energy/raw', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(requestBody)
            });

            if (!res.ok) {
                const errData = await res.json().catch(() => ({}));
                throw new Error(errData.error || `Server responded with status ${res.status}`);
            }

            const data = await res.json();
            renderResults(data);
        } catch (err) {
            showError(`Optimization Failed: ${err.message}`);
            resultsPlaceholder.style.display = 'block';
        } finally {
            loadingSpinner.style.display = 'none';
            runOptimizeBtn.disabled = false;
        }
    });

    function renderResults(data) {
        // Summary Metrics
        summaryGridKwh.textContent = `${(data.total_grid_kwh || 0).toFixed(2)} kWh`;
        summaryCostBdt.textContent = `${(data.total_cost_bdt || 0).toFixed(2)} BDT`;
        summaryPeakKwh.textContent = `${(data.peak_grid_kwh || 0).toFixed(2)} kWh`;
        summaryText.textContent = data.plan_summary || 'No summary provided.';

        // Directives
        directivesContainer.innerHTML = '';
        if (data.directive_interpretation && data.directive_interpretation.length > 0) {
            data.directive_interpretation.forEach((d) => {
                const badgeClass = getBadgeClass(d.directive_type);
                const card = document.createElement('div');
                card.className = 'directive-card';

                let adjHtml = '';
                if (d.structured_adjustment) {
                    const sa = d.structured_adjustment;
                    const parts = [];
                    if (sa.hours) parts.push(`Hours: [${sa.hours.join(', ')}]`);
                    if (sa.factor != null) parts.push(`Factor: ${sa.factor}`);
                    if (sa.minimum_energy_kwh != null) parts.push(`Min Reserve: ${sa.minimum_energy_kwh} kWh`);
                    if (sa.max_grid_kwh != null) parts.push(`Max Grid: ${sa.max_grid_kwh} kWh`);
                    if (parts.length > 0) {
                        adjHtml = `<div class="directive-adjustment"><code>${parts.join(' | ')}</code></div>`;
                    }
                }

                card.innerHTML = `
                    <div class="directive-header">
                        <span class="directive-title">Note #${d.note_index + 1} (${d.applies ? 'Applied' : 'Ignored'})</span>
                        <span class="badge ${badgeClass}">${escapeHtml(d.directive_type || 'unknown')}</span>
                    </div>
                    <p class="directive-explanation">${escapeHtml(d.explanation || '')}</p>
                    ${adjHtml}
                `;
                directivesContainer.appendChild(card);
            });
        } else {
            directivesContainer.innerHTML = '<p class="text-muted">No directives interpreted.</p>';
        }

        // Hourly Plan Table
        planTableBody.innerHTML = '';
        if (data.hourly_plan && data.hourly_plan.length > 0) {
            data.hourly_plan.forEach((row) => {
                const tr = document.createElement('tr');
                const actionClass = `badge-action-${(row.battery_action || '').toLowerCase()}`;
                tr.innerHTML = `
                    <td class="font-bold">${row.hour}:00</td>
                    <td>${(row.grid_kwh || 0).toFixed(2)}</td>
                    <td>${(row.solar_used_kwh || 0).toFixed(2)}</td>
                    <td><span class="badge ${actionClass}">${escapeHtml(row.battery_action || 'NONE')}</span></td>
                    <td>${(row.battery_kwh || 0).toFixed(2)}</td>
                    <td>${(row.battery_energy_after_kwh || 0).toFixed(2)}</td>
                `;
                planTableBody.appendChild(tr);
            });
        }

        resultsContent.style.display = 'block';
    }

    function getBadgeClass(type) {
        switch (type) {
            case 'solar_reduction': return 'badge-solar-reduction';
            case 'no_charge_window': return 'badge-no-charge-window';
            case 'no_discharge_window': return 'badge-no-discharge-window';
            case 'minimum_battery_reserve': return 'badge-minimum-battery-reserve';
            case 'max_grid_window': return 'badge-max-grid-window';
            default: return 'badge-no-op';
        }
    }

    function showError(msg) {
        errorMessage.textContent = msg;
        errorBanner.style.display = 'flex';
    }

    function hideError() {
        errorBanner.style.display = 'none';
    }

    function escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }
});
