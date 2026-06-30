const API_BASE = "";
let refreshInterval;
let currentUser = "";

async function fetchCurrentStatus() {
    try {
        const response = await fetch(`${API_BASE}/api/current`);
        const data = await response.json();
        currentUser = data.userName;
        updateStatusBar(data);
    } catch (error) {
        console.error("Failed to fetch current status:", error);
    }
}

async function fetchProjects() {
    try {
        const response = await fetch(`${API_BASE}/api/projects`);
        const projects = await response.json();
        renderProjects(projects);
    } catch (error) {
        console.error("Failed to fetch projects:", error);
        document.getElementById("projects-container").innerHTML = '<p class="loading">Failed to load projects. Is the service running?</p>';
    }
}

function updateStatusBar(status) {
    const statusText = document.getElementById("status-text");
    const indicator = document.querySelector(".status-indicator");

    indicator.className = "status-indicator";

    if (status.isTracking) {
        if (status.state === "GraceStart") {
            statusText.textContent = `Grace Start: "${status.projectName}" (not tracking yet)`;
            indicator.classList.add("gracestart");
        } else if (status.state === "GraceEnd") {
            statusText.textContent = `Tracking: "${status.projectName}" [Grace Period]`;
            indicator.classList.add("graceend");
        } else {
            const pageLabel     = status.page     ? ` · ${formatPageName(status.page)}`     : "";
            const timelineLabel = status.timeline ? ` · ${status.timeline}` : "";
            const renderNote    = status.isRendering ? " ⟳ rendering" : "";
            statusText.textContent = `${status.projectName}${pageLabel}${timelineLabel}${renderNote}`;
            indicator.classList.add("tracking");
        }
    } else if (!status.isResolveRunning) {
        statusText.textContent = "DaVinci not open";
        indicator.classList.add("idle");
    } else if (status.isResolveRunning && !status.liveProject) {
        statusText.textContent = "DaVinci open — no project loaded";
        indicator.classList.add("idle");
    } else {
        // Resolve running + project open, but session not active (idle/unfocused)
        const pageLabel = status.page ? ` · ${formatPageName(status.page)}` : "";
        statusText.textContent = `Not tracking: "${status.liveProject}"${pageLabel}`;
        indicator.classList.add("idle");
    }
}

function formatTimeSpan(totalSeconds) {
    const hours = Math.floor(totalSeconds / 3600);
    const minutes = Math.floor((totalSeconds % 3600) / 60);

    if (hours > 0) {
        return `${hours}h ${minutes}m`;
    }
    return `${minutes}m`;
}

function formatLastActivity(dateString) {
    // Ensure UTC timezone is specified for proper parsing
    if (!dateString.endsWith("Z") && !dateString.includes("+") && !dateString.includes("-", 10)) {
        dateString += "Z";
    }
    const date = new Date(dateString);
    const now = new Date();
    const diffMs = now - date;
    const diffMins = Math.floor(diffMs / 60000);
    const diffHours = Math.floor(diffMins / 60);
    const diffDays = Math.floor(diffHours / 24);

    if (diffMins < 1) return "Just now";
    if (diffMins < 60) return `${diffMins} min ago`;
    if (diffHours < 24) return `${diffHours} hour${diffHours > 1 ? "s" : ""} ago`;
    if (diffDays === 1) return "Yesterday";
    if (diffDays < 7) return `${diffDays} days ago`;

    return date.toLocaleDateString();
}

function renderProjects(projects) {
    const container = document.getElementById("projects-container");

    if (projects.length === 0) {
        container.innerHTML = '<p class="no-projects">No projects tracked yet. Open DaVinci Resolve to start tracking!</p>';
        return;
    }

    // Group projects by project name
    const projectGroups = {};
    projects.forEach(stat => {
        if (!projectGroups[stat.projectName]) {
            projectGroups[stat.projectName] = {
                projectName: stat.projectName,
                users: [],
                lastActivity: stat.lastActivity,
                hasActiveTracking: false
            };
        }
        projectGroups[stat.projectName].users.push(stat);
        if (stat.isCurrentlyTracking) {
            projectGroups[stat.projectName].hasActiveTracking = true;
        }
        // Update last activity to most recent
        if (new Date(stat.lastActivity) > new Date(projectGroups[stat.projectName].lastActivity)) {
            projectGroups[stat.projectName].lastActivity = stat.lastActivity;
        }
    });

    // Convert to array and sort by last activity
    const sortedProjects = Object.values(projectGroups)
        .sort((a, b) => new Date(b.lastActivity) - new Date(a.lastActivity));

    container.innerHTML = sortedProjects
        .map(projectGroup => {
            const cardClass = projectGroup.hasActiveTracking ? "currently-tracking" : "";
            
            const usersHtml = projectGroup.users
                .sort((a, b) => new Date(b.lastActivity) - new Date(a.lastActivity))
                .map(user => {
                    const isCurrentUser = user.userName === currentUser;
                    const userBadge = isCurrentUser ? '<span class="user-badge-you">You</span>' : '';
                    
                    let trackingBadge = "";
                    if (user.isCurrentlyTracking) {
                        if (user.currentState === "GraceStart") {
                            trackingBadge = '<span class="tracking-badge grace-start">⏱ Grace Start</span>';
                        } else if (user.currentState === "GraceEnd") {
                            trackingBadge = '<span class="tracking-badge grace-end">⏳ Grace Period</span>';
                        } else {
                            trackingBadge = '<span class="tracking-badge">● Tracking</span>';
                        }
                    }
                    
                    const activityBreakdownHtml = renderActivityBreakdown(user.activityBreakdown, user.currency);

                    return `
                        <div class="user-stat ${isCurrentUser ? 'current-user' : ''}">
                            <div class="user-info">
                                <span class="user-name">${escapeHtml(user.userName)}</span>
                                ${userBadge}
                                ${trackingBadge}
                            </div>
                            <div class="user-time-info">
                                <span class="user-time">${formatTimeSpan((user.totalWorkTime || user.totalElapsedTime).totalSeconds)}</span>
                                <span class="user-time-label">active</span>
                                ${user.totalProcessingTime && user.totalProcessingTime.totalSeconds > 30 ? `
                                    <span class="user-processing-sep">+</span>
                                    <span class="user-processing-time" title="Time waiting for processing (renders, etc.) to complete">${formatTimeSpan(user.totalProcessingTime.totalSeconds)}</span>
                                    <span class="user-processing-label">processing</span>
                                ` : ''}
                                ${user.totalCost != null && user.totalCost > 0 ? `
                                    <span class="user-cost-sep">=</span>
                                    <span class="user-cost" title="Calculated cost based on billing rates">${formatCost(user.totalCost, user.currency)}</span>
                                ` : ''}
                                <span class="user-sessions">${user.sessionCount} session${user.sessionCount !== 1 ? 's' : ''}</span>
                                <span class="user-last-activity">${formatLastActivity(user.lastActivity)}</span>
                            </div>
                            ${activityBreakdownHtml}
                        </div>
                    `;
                })
                .join("");

            // Project-level billing row
            const calculatedTotal = projectGroup.users.reduce((sum, u) => sum + (u.totalCost || 0), 0);
            const currency        = projectGroup.users.find(u => u.currency)?.currency || '';
            const billedAmount    = projectGroup.users[0]?.billedAmount ?? null;
            const billingRowHtml  = renderProjectBillingRow(
                escapeHtml(projectGroup.projectName), calculatedTotal, currency, billedAmount);

            return `
                <div class="project-card ${cardClass}">
                    <div class="project-header">
                        <div class="project-name">
                            ${escapeHtml(projectGroup.projectName)}
                        </div>
                        <button class="btn-delete" onclick="showDeleteConfirmation('${escapeHtml(projectGroup.projectName)}')" title="Delete project (all users)">
                            🗑️
                        </button>
                    </div>
                    ${billingRowHtml}
                    <div class="users-container">
                        ${usersHtml}
                    </div>
                </div>
            `;
        })
        .join("");
}

function formatPageName(page) {
    const map = {
        color: "Color", edit: "Edit", cut: "Cut", media: "Media",
        fusion: "Fusion", fairlight: "Fairlight", deliver: "Deliver", photo: "Photo"
    };
    return map[page] || (page.charAt(0).toUpperCase() + page.slice(1));
}

// ── Activity breakdown ────────────────────────────────────────────────────────

const ACTIVITY_COLORS = {
    color:     "#8b5cf6",   // violet
    edit:      "#3b82f6",   // blue
    cut:       "#06b6d4",   // cyan
    media:     "#64748b",   // slate
    fusion:    "#f97316",   // orange
    fairlight: "#10b981",   // emerald
    deliver:   "#6366f1",   // indigo
    photo:     "#ec4899",   // pink
    render:    "#94a3b8",   // gray — processing activity
    unknown:   "#94a3b8",   // gray
};

const ACTIVITY_LABELS = {
    color:     "Color",
    edit:      "Edit",
    cut:       "Cut",
    media:     "Media",
    fusion:    "Fusion",
    fairlight: "Fairlight",
    deliver:   "Deliver",
    photo:     "Photo",
    render:    "Render",
};

function renderActivityBreakdown(breakdown, currency) {
    if (!breakdown || breakdown.length === 0) return "";

    const chips = breakdown.map(a => {
        const isProcessing = a.kind === "Processing";
        const color        = ACTIVITY_COLORS[a.activityType] || ACTIVITY_COLORS.unknown;
        const label        = ACTIVITY_LABELS[a.activityType] || a.activityType;
        const displayTime  = formatTimeSpan(a.totalTime.totalSeconds);
        const timelines    = (a.timelines && a.timelines.length > 0)
            ? a.timelines.join(" · ")
            : "";

        // Build tooltip
        const kindLabel = isProcessing ? "processing" : "active";
        let tooltip = `${label}: ${displayTime} ${kindLabel} (${a.percentage}%)`;
        if (a.cost != null && a.cost > 0) tooltip += `\n${formatCost(a.cost, currency)}`;
        if (timelines) tooltip += `\n${timelines}`;

        if (isProcessing) {
            // Processing chips: ⟳ icon, muted dashed style — no --chip-color tinting
            return `<span class="activity-chip activity-chip--processing" title="${escapeHtml(tooltip)}">
                ⟳ ${label} <span class="activity-chip-meta">${displayTime} · ${a.percentage}%</span>
            </span>`;
        }

        return `<span class="activity-chip" style="--chip-color:${color}" title="${escapeHtml(tooltip)}">
            <span class="activity-chip-dot"></span>${label} <span class="activity-chip-meta">${displayTime} · ${a.percentage}%</span>
        </span>`;
    }).join("");

    return `<div class="activity-chips">${chips}</div>`;
}

function escapeHtml(text) {
    const div = document.createElement("div");
    div.textContent = text;
    return div.innerHTML;
}

function showDeleteConfirmation(projectName) {
    const modal = document.getElementById("delete-modal");
    const projectNameElement = document.getElementById("delete-project-name");
    projectNameElement.textContent = projectName;
    modal.style.display = "flex";
}

function hideDeleteModal() {
    const modal = document.getElementById("delete-modal");
    modal.style.display = "none";
}

async function deleteProject(projectName) {
    try {
        const response = await fetch(`${API_BASE}/api/projects/${encodeURIComponent(projectName)}`, {
            method: "DELETE",
        });

        const result = await response.json();

        if (result.success) {
            hideDeleteModal();
            showNotification(`Deleted ${result.deletedSessions} session(s) for "${projectName}"`, "success");
            await refresh();
        } else {
            showNotification(`Failed to delete project: ${result.message}`, "error");
        }
    } catch (error) {
        console.error("Failed to delete project:", error);
        showNotification("Failed to delete project. Please try again.", "error");
    }
}

function showNotification(message, type = "info") {
    // Simple notification - you can enhance this
    const notification = document.createElement("div");
    notification.className = `notification notification-${type}`;
    notification.textContent = message;
    document.body.appendChild(notification);

    setTimeout(() => {
        notification.classList.add("show");
    }, 10);

    setTimeout(() => {
        notification.classList.remove("show");
        setTimeout(() => notification.remove(), 300);
    }, 3000);
}

async function refresh() {
    await Promise.all([fetchCurrentStatus(), fetchProjects()]);
}

// Modal event listeners
document.addEventListener("DOMContentLoaded", () => {
    const confirmBtn = document.getElementById("delete-confirm-btn");
    const cancelBtn = document.getElementById("delete-cancel-btn");
    const modal = document.getElementById("delete-modal");

    confirmBtn.addEventListener("click", () => {
        const projectName = document.getElementById("delete-project-name").textContent;
        deleteProject(projectName);
    });

    cancelBtn.addEventListener("click", hideDeleteModal);

    // Close modal on background click
    modal.addEventListener("click", e => {
        if (e.target === modal) {
            hideDeleteModal();
        }
    });
});

// ── Tab navigation ────────────────────────────────────────────────────────────

function switchTab(name, btn) {
    document.querySelectorAll(".tab-pane").forEach(p => p.style.display = "none");
    document.querySelectorAll(".tab").forEach(t => t.classList.remove("active"));
    document.getElementById(`tab-${name}`).style.display = "block";
    btn.classList.add("active");
    if (name === "toggles")       fetchNodeToggles();
    if (name === "settings")      fetchSettings();
    if (name === "troubleshooter") runDiagnostics();
}

// ── Initial load ──────────────────────────────────────────────────────────────

// Initial load
refresh();
fetchBridgeHealth();

// Auto-refresh every 5 seconds
refreshInterval = setInterval(refresh, 5000);
// Health pill refreshes every 15 seconds
setInterval(fetchBridgeHealth, 15000);

// ── Node Actions Panel ────────────────────────────────────────────────────────

let editingToggleId = null; // null = creating new

async function fetchNodeToggles() {
    try {
        const response = await fetch(`${API_BASE}/api/node-toggles`);
        const groups = await response.json();
        renderNodeToggles(groups);
    } catch (error) {
        console.error("Failed to fetch node actions:", error);
        document.getElementById("toggles-container").innerHTML =
            '<p class="loading">Failed to load node actions.</p>';
    }
}

function renderNodeToggles(groups) {
    const container = document.getElementById("toggles-container");
    if (!groups || groups.length === 0) {
        container.innerHTML = '<p class="no-projects">No actions configured. Click "+ Add Action" to create one.</p>';
        return;
    }

    container.innerHTML = groups.map(g => {
        const isSelect = g.actionType === "Select";

        const stateLabel = isSelect
            ? '<span class="toggle-state select-type" title="Select action — navigates to the target node">→ Select</span>'
            : (g.currentEnabled === true  ? '<span class="toggle-state on">ON</span>'
            : g.currentEnabled === false ? '<span class="toggle-state off">OFF</span>'
            : '<span class="toggle-state unknown" title="Press Test once to sync (first press always disables)">? unknown</span>');

        const nodeList = (g.nodes || []).map(n => {
            const id = n.nodeId != null ? `#${n.nodeId}` : "";
            const title = n.title ? escapeHtml(n.title) : "";
            const label = [id, title].filter(Boolean).join(" ");
            return `<span class="node-chip">${label} <em>(${n.level})</em></span>`;
        }).join(" ");

        const syncBtn = isSelect ? '' :
            `<button class="btn btn-sm btn-sync" onclick="syncState('${g.id}')" title="Tell the app nodes are currently ON (sync after manual DaVinci change)">↺ Sync ON</button>`;

        const testTitle = isSelect ? "Navigate to target node now" : "Toggle nodes now (alternates ON/OFF)";

        return `
        <div class="toggle-card">
            <div class="toggle-header">
                <div class="toggle-info">
                    <span class="toggle-name">${escapeHtml(g.name)}</span>
                    <span class="hotkey-badge">${escapeHtml(g.hotkey || "(no hotkey)")}</span>
                    ${stateLabel}
                </div>
                <div class="toggle-actions">
                    <button class="btn btn-sm btn-test" onclick="executeAction('${g.id}', ${isSelect})" title="${testTitle}">▶ Test</button>
                    ${syncBtn}
                    <button class="btn btn-sm btn-secondary" onclick="editToggleGroup('${g.id}')" title="Edit">✏️</button>
                    <button class="btn-delete" onclick="deleteToggleGroup('${g.id}', '${escapeHtml(g.name)}')" title="Delete">🗑️</button>
                </div>
            </div>
            <div class="toggle-nodes">${nodeList || '<em class="muted">No nodes configured</em>'}</div>
        </div>`;
    }).join("");
}

async function syncState(id) {
    try {
        await fetch(`${API_BASE}/api/node-toggles/${id}/reset-state?assumeEnabled=true`, { method: "POST" });
        showNotification("State reset → assumed ON (next Test will disable)", "info");
        fetchNodeToggles();
    } catch { showNotification("Sync failed", "error"); }
}

async function executeAction(id, isSelect = false) {
    try {
        const response = await fetch(`${API_BASE}/api/node-toggles/${id}/execute`, { method: "POST" });
        const result = await response.json();
        if (result.success) {
            if (isSelect) {
                showNotification(`Selected node ${result.nodeIndex}`, "success");
            } else {
                const state = result.enabled ? "enabled" : "disabled";
                showNotification(`Toggled → ${state}`, "success");
            }
            fetchNodeToggles();
        } else {
            showNotification(result.message || "Action failed — check logs", "error");
        }
    } catch (error) {
        showNotification("Action request failed", "error");
    }
}

function showToggleModal(group = null) {
    editingToggleId = group ? group.id : null;
    document.getElementById("toggle-modal-title").textContent = group ? "Edit Node Action" : "Add Node Action";
    document.getElementById("toggle-name").value = group ? group.name : "";
    document.getElementById("toggle-hotkey").value = group ? group.hotkey : "";

    // Action type selector
    const isSelect = group?.actionType === "Select";
    document.getElementById("toggle-action-type").value = isSelect ? "Select" : "Toggle";
    _updateModalForActionType(isSelect);

    const nodesList = document.getElementById("toggle-nodes-list");
    nodesList.innerHTML = "";
    const nodes = group ? group.nodes : [];
    if (nodes.length === 0) addNodeRow();
    else nodes.forEach(n => addNodeRow(n));

    document.getElementById("toggle-modal").style.display = "flex";
}

function _updateModalForActionType(isSelect) {
    const addBtn = document.querySelector('#toggle-modal button[onclick="addNodeRow()"]');
    const nodesLabel = document.querySelector('#toggle-modal label[for="toggle-nodes-list"], #toggle-modal .form-group label');
    if (addBtn) addBtn.style.display = isSelect ? "none" : "";

    // Keep only the first node row for Select
    if (isSelect) {
        const rows = document.querySelectorAll("#toggle-nodes-list .node-row");
        rows.forEach((r, i) => { if (i > 0) r.remove(); });
    }
}

function hideToggleModal() {
    document.getElementById("toggle-modal").style.display = "none";
    const hint = document.getElementById("nodes-loaded-hint");
    if (hint) { hint.style.display = "none"; hint.textContent = ""; }
    _availableNodes = [];
    editingToggleId = null;
}

function addNodeRow(node = null) {
    const container = document.getElementById("toggle-nodes-list");
    const row = document.createElement("div");
    row.className = "node-row";
    // All four levels — PreClip/PostClip via ColorGroup API (clip must be in a Color Group)
    const levels = ["Timeline", "Clip", "PreClip", "PostClip"];
    const currentLevel = node?.level ?? "Timeline";
    const levelOpts = levels.map(l =>
        `<option value="${l}" ${currentLevel === l ? "selected" : ""}>${l}</option>`
    ).join("");

    if (_availableNodes.length > 0) {
        // Picker mode: level dropdown → filtered node select
        row.innerHTML = `
            <select class="node-level-select" onchange="refreshPickerRow(this.closest('.node-row'))">
                ${levelOpts}
            </select>
            <select class="node-picker-select">
                <option value="">— select node —</option>
            </select>
            <button class="btn-delete" onclick="this.closest('.node-row').remove()" title="Remove">✕</button>
        `;
        container.appendChild(row);
        refreshPickerRow(row, node); // populate + pre-select
    } else {
        // Manual mode: Level first (defines scope), then ID + title
        row.innerHTML = `
            <select class="node-level-select">
                ${levelOpts}
            </select>
            <input type="number" class="node-id-input" placeholder="Node ID" min="1"
                value="${node?.nodeId != null ? node.nodeId : ''}" title="Node ID (optional, more stable)" />
            <input type="text" class="node-title-input" placeholder="Node Title"
                value="${node ? escapeHtml(node.title || '') : ''}" title="Node title" />
            <button class="btn-delete" onclick="this.closest('.node-row').remove()" title="Remove">✕</button>
        `;
        container.appendChild(row);
    }
}

function refreshPickerRow(row, preselect = null) {
    const level = row.querySelector(".node-level-select").value;
    const picker = row.querySelector(".node-picker-select");
    if (!picker) return;

    const filtered = _availableNodes.filter(n => n.level === level);
    picker.innerHTML = '<option value="">— select node —</option>' +
        filtered.map(n => {
            const idPart = n.nodeId != null ? `#${n.nodeId}: ` : "";
            const label = idPart + (n.title || "(unnamed)");
            const sel = preselect && (
                (preselect.nodeId != null && n.nodeId === preselect.nodeId) ||
                (preselect.title && n.title?.toLowerCase() === preselect.title.toLowerCase())
            ) ? "selected" : "";
            return `<option value="${n.nodeId ?? ""}" data-title="${escapeHtml(n.title || "")}" ${sel}>${escapeHtml(label)}</option>`;
        }).join("");
}

async function saveToggleGroup() {
    const name = document.getElementById("toggle-name").value.trim();
    const hotkey = document.getElementById("toggle-hotkey").value.trim();

    if (!name) { showNotification("Group name is required", "error"); return; }

    const nodeRows = document.querySelectorAll("#toggle-nodes-list .node-row");
    const nodes = [];
    let valid = true;

    nodeRows.forEach(row => {
        const level = row.querySelector(".node-level-select").value;
        const pickerSelect = row.querySelector(".node-picker-select");

        if (pickerSelect) {
            // Picker mode — read from the select
            const opt = pickerSelect.options[pickerSelect.selectedIndex];
            if (!opt || !opt.value && !opt.dataset.title) { valid = false; return; }
            nodes.push({
                nodeId: opt.value ? parseInt(opt.value, 10) : null,
                title: opt.dataset.title || null,
                level
            });
        } else {
            // Manual mode
            const nodeId = row.querySelector(".node-id-input").value.trim();
            const title  = row.querySelector(".node-title-input").value.trim();
            if (!nodeId && !title) { valid = false; return; }
            nodes.push({
                nodeId: nodeId ? parseInt(nodeId, 10) : null,
                title: title || null,
                level
            });
        }
    });

    if (!valid) { showNotification("Each node needs at least a Node ID or Title", "error"); return; }

    const actionType = document.getElementById("toggle-action-type")?.value || "Toggle";
    const group = { name, hotkey, actionType, nodes };
    if (editingToggleId) group.id = editingToggleId;

    try {
        const method = editingToggleId ? "PUT" : "POST";
        const url = editingToggleId
            ? `${API_BASE}/api/node-toggles/${editingToggleId}`
            : `${API_BASE}/api/node-toggles`;

        const response = await fetch(url, {
            method,
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(group)
        });
        const result = await response.json();
        if (result.success) {
            hideToggleModal();
            showNotification(editingToggleId ? "Group updated" : "Group created", "success");
            fetchNodeToggles();
        } else {
            showNotification(result.message || "Save failed", "error");
        }
    } catch (error) {
        showNotification("Save request failed", "error");
    }
}

async function editToggleGroup(id) {
    try {
        const response = await fetch(`${API_BASE}/api/node-toggles`);
        const groups = await response.json();
        const group = groups.find(g => g.id === id);
        if (group) showToggleModal(group);
    } catch (error) {
        showNotification("Failed to load group", "error");
    }
}

async function deleteToggleGroup(id, name) {
    if (!confirm(`Delete toggle group "${name}"?`)) return;
    try {
        const response = await fetch(`${API_BASE}/api/node-toggles/${id}`, { method: "DELETE" });
        const result = await response.json();
        if (result.success) {
            showNotification(`Deleted "${name}"`, "success");
            fetchNodeToggles();
        } else {
            showNotification(result.message || "Delete failed", "error");
        }
    } catch (error) {
        showNotification("Delete request failed", "error");
    }
}

// ── Node picker (load from DaVinci) ──────────────────────────────────────────

let _availableNodes = []; // cached after load

async function loadNodesFromDaVinci() {
    const btn = document.getElementById("load-nodes-btn");
    btn.textContent = "⟳ Loading…";
    btn.disabled = true;

    try {
        const response = await fetch(`${API_BASE}/api/node-toggles/available-nodes`);
        const nodes = await response.json();

        if (!Array.isArray(nodes) || nodes.length === 0) {
            showNotification("No nodes found — is DaVinci open on the Color page with a timeline?", "error");
            return;
        }

        _availableNodes = nodes;

        // Upgrade any existing manual rows to picker rows
        const existingRows = [...document.querySelectorAll("#toggle-nodes-list .node-row")];
        existingRows.forEach(row => {
            const nodeId = row.querySelector(".node-id-input")?.value.trim();
            const title  = row.querySelector(".node-title-input")?.value.trim();
            const level  = row.querySelector(".node-level-select")?.value ?? "Timeline";
            const preselect = (nodeId || title) ? { nodeId: nodeId ? parseInt(nodeId) : null, title } : null;
            row.remove();
            addNodeRow(preselect ? { nodeId: preselect.nodeId, title: preselect.title, level } : { level });
        });
        if (existingRows.length === 0) addNodeRow(); // add one picker row if list was empty

        const hint = document.getElementById("nodes-loaded-hint");
        hint.textContent = `✓ ${nodes.length} nodes loaded`;
        hint.style.display = "inline";

        showNotification(`Loaded ${nodes.length} node(s) — select from the dropdowns`, "success");
    } catch (error) {
        showNotification("Failed to load nodes — check DaVinci is running", "error");
    } finally {
        btn.textContent = "⟳ Load from DaVinci";
        btn.disabled = false;
    }
}

// Close toggle modal on background click
document.addEventListener("DOMContentLoaded", () => {
    const toggleModal = document.getElementById("toggle-modal");
    toggleModal.addEventListener("click", e => {
        if (e.target === toggleModal) hideToggleModal();
    });
});

// ── Billing helpers ───────────────────────────────────────────────────────────

function formatCost(amount, currency) {
    if (amount == null) return '';
    const sym = currency || '';
    return `${sym} ${Number(amount).toFixed(2)}`.trim();
}

// ── Project billing row ───────────────────────────────────────────────────────

function renderProjectBillingRow(projectNameEscaped, calculatedTotal, currency, billedAmount) {
    const hasCalc   = calculatedTotal > 0 && currency;
    const hasBilled = billedAmount != null;

    if (!hasCalc && !hasBilled) return ''; // nothing to show

    const calcHtml = hasCalc
        ? `<span class="billing-label">Calculated</span><span class="billing-value">${formatCost(calculatedTotal, currency)}</span>`
        : '';

    const billedVal  = hasBilled ? Number(billedAmount).toFixed(2) : '';
    const billedHtml = `
        <span class="billing-label">Billed</span>
        <span class="billing-billed-wrap" id="billing-wrap-${projectNameEscaped}">
            <span class="billing-billed-display" onclick="startEditBilling('${projectNameEscaped}', ${billedVal || 'null'})"
                  title="Click to edit">${hasBilled ? formatCost(billedAmount, currency) : '<em class="muted">not set</em>'} ✎</span>
            <span class="billing-billed-edit" style="display:none">
                <input type="number" class="billing-input" id="billing-input-${projectNameEscaped}"
                    value="${billedVal}" min="0" step="0.01" placeholder="0.00" />
                <button class="btn btn-sm btn-primary" onclick="saveBilling('${projectNameEscaped}', '${currency}')">✓</button>
                <button class="btn btn-sm btn-secondary" onclick="cancelEditBilling('${projectNameEscaped}')">✕</button>
            </span>
        </span>`;

    let diffHtml = '';
    if (hasCalc && hasBilled) {
        const diff = Number(billedAmount) - calculatedTotal;
        const absDiff = Math.abs(diff).toFixed(2);
        if (diff > 0.01) {
            diffHtml = `<span class="billing-diff billing-overshoot" title="Billed more than calculated — good">↑ +${currency} ${absDiff} overshoot</span>`;
        } else if (diff < -0.01) {
            diffHtml = `<span class="billing-diff billing-undershoot" title="Billed less than calculated">↓ -${currency} ${absDiff} undershoot</span>`;
        } else {
            diffHtml = `<span class="billing-diff billing-on-target">= on target</span>`;
        }
    }

    return `<div class="project-billing-row">${calcHtml}${billedHtml}${diffHtml}</div>`;
}

function startEditBilling(projectName, currentVal) {
    const wrap  = document.getElementById(`billing-wrap-${projectName}`);
    const input = document.getElementById(`billing-input-${projectName}`);
    wrap.querySelector('.billing-billed-display').style.display = 'none';
    wrap.querySelector('.billing-billed-edit').style.display    = 'inline-flex';
    input.value = currentVal != null ? currentVal : '';
    input.focus();
}

function cancelEditBilling(projectName) {
    const wrap = document.getElementById(`billing-wrap-${projectName}`);
    wrap.querySelector('.billing-billed-display').style.display = 'inline';
    wrap.querySelector('.billing-billed-edit').style.display    = 'none';
}

async function saveBilling(projectName, currency) {
    const input  = document.getElementById(`billing-input-${projectName}`);
    const valStr = input.value.trim();
    const amount = valStr === '' ? null : parseFloat(valStr);

    try {
        if (amount === null) {
            await fetch(`${API_BASE}/api/projects/${encodeURIComponent(projectName)}/billing`, { method: 'DELETE' });
        } else {
            await fetch(`${API_BASE}/api/projects/${encodeURIComponent(projectName)}/billing`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ amount })
            });
        }
        await refresh();
    } catch (e) {
        showNotification('Failed to save billed amount', 'error');
    }
}

// ── Settings tab ──────────────────────────────────────────────────────────────

const KNOWN_ACTIVITY_TYPES = [
    { type: 'edit',      kind: 'User',       label: 'Edit'       },
    { type: 'color',     kind: 'User',       label: 'Color'      },
    { type: 'cut',       kind: 'User',       label: 'Cut'        },
    { type: 'deliver',   kind: 'User',       label: 'Deliver'    },
    { type: 'media',     kind: 'User',       label: 'Media'      },
    { type: 'fusion',    kind: 'User',       label: 'Fusion'     },
    { type: 'fairlight', kind: 'User',       label: 'Fairlight'  },
    { type: 'photo',     kind: 'User',       label: 'Photo'      },
    { type: 'render',    kind: 'Processing', label: 'Render'     },
];

async function fetchSettings() {
    try {
        const res      = await fetch(`${API_BASE}/api/settings`);
        const settings = await res.json();
        renderSettings(settings);
    } catch (e) {
        document.getElementById('settings-container').innerHTML =
            '<p class="loading">Failed to load settings.</p>';
    }
}

function renderSettings(s) {
    const billing = s.billing || {};
    const rates   = billing.activityTypeRates || {};

    const rateRows = KNOWN_ACTIVITY_TYPES.map(a => {
        const val = rates[a.type] != null ? rates[a.type] : '';
        const kindBadge = a.kind === 'Processing'
            ? '<span class="kind-badge processing">Processing</span>'
            : '<span class="kind-badge user">User</span>';
        return `<tr>
            <td>${a.label} ${kindBadge}</td>
            <td><input type="number" class="settings-input-sm" id="rate-${a.type}"
                       value="${val}" min="0" step="0.01" placeholder="(kind default)" /></td>
        </tr>`;
    }).join('');

    document.getElementById('settings-container').innerHTML = `
        <div class="settings-form">

            <div class="settings-section-header">
                <h3>Tracking</h3>
                <p class="settings-restart-banner">⚠ Changes to tracking settings require an app restart to take effect.</p>
            </div>

            <div class="settings-field">
                <label>Grace Start <span class="settings-unit">seconds</span></label>
                <p class="settings-hint">How long you must stay focused before a new session is confirmed.</p>
                <input type="number" id="setting-graceStartSeconds" class="settings-input"
                       value="${s.graceStartSeconds ?? 30}" min="1" max="300" />
            </div>

            <div class="settings-field">
                <label>Grace End <span class="settings-unit">minutes</span></label>
                <p class="settings-hint">How long a session continues after you stop working before it closes.</p>
                <input type="number" id="setting-graceEndMinutes" class="settings-input"
                       value="${s.graceEndMinutes ?? 10}" min="1" max="120" />
            </div>

            <div class="settings-field">
                <label>Inactivity Threshold <span class="settings-unit">minutes</span></label>
                <p class="settings-hint">Minutes of no keyboard/mouse input before you are considered idle.</p>
                <input type="number" id="setting-inactivityThresholdMinutes" class="settings-input"
                       value="${s.inactivityThresholdMinutes ?? 1}" min="1" max="60" />
            </div>

            <hr class="settings-divider" />

            <div class="settings-section-header">
                <h3>Node Actions</h3>
                <p class="settings-hint">Keyboard shortcuts DaVinci uses for node navigation. Must match what you have assigned in DaVinci Resolve → Keyboard Customization → Color → Nodes.</p>
            </div>

            <div class="settings-field">
                <label>Append Serial Node shortcut</label>
                <p class="settings-hint">Used by Select actions to create a temporary anchor node. DaVinci default: Alt+S.</p>
                <input type="text" id="setting-appendNodeShortcut" class="settings-input"
                       value="${s.appendNodeShortcut ?? 'Alt+S'}" placeholder="Alt+S" />
            </div>

            <div class="settings-field">
                <label>Next Node shortcut</label>
                <p class="settings-hint">Used by Select actions to navigate forward through nodes. DaVinci Windows default: Alt+Shift+' (tick).</p>
                <input type="text" id="setting-nextNodeShortcut" class="settings-input"
                       value="${s.nextNodeShortcut ?? 'Alt+Shift+Oem7'}" placeholder="Alt+Shift+Oem7" />
            </div>

            <hr class="settings-divider" />

            <div class="settings-section-header">
                <h3>Billing</h3>
                <p class="settings-hint">Configure hourly rates to calculate project costs. Leave rates at 0 to disable cost display.</p>
            </div>

            <div class="settings-field">
                <label>Currency</label>
                <p class="settings-hint">ISO 4217 code shown on all cost displays (e.g. USD, EUR, CHF).</p>
                <input type="text" id="setting-currency" class="settings-input settings-input-sm"
                       value="${billing.currency ?? 'USD'}" maxlength="5" placeholder="USD"
                       oninput="this.value = this.value.toUpperCase()" />
            </div>

            <div class="settings-field">
                <label>Default User activity rate <span class="settings-unit">per hour</span></label>
                <p class="settings-hint">Applied to any User activity without a specific override below.</p>
                <input type="number" id="setting-defaultUserRate" class="settings-input"
                       value="${billing.defaultUserRatePerHour ?? 0}" min="0" step="0.01" />
            </div>

            <div class="settings-field">
                <label>Default Processing activity rate <span class="settings-unit">per hour</span></label>
                <p class="settings-hint">Applied to renders and other machine processing without a specific override.</p>
                <input type="number" id="setting-defaultProcessingRate" class="settings-input"
                       value="${billing.defaultProcessingRatePerHour ?? 0}" min="0" step="0.01" />
            </div>

            <div class="settings-field">
                <label>Per-activity overrides <span class="settings-unit">per hour</span></label>
                <p class="settings-hint">Leave blank to use the kind default above.</p>
                <table class="settings-rates-table">
                    <thead><tr><th>Activity</th><th>Rate / hour</th></tr></thead>
                    <tbody>${rateRows}</tbody>
                </table>
            </div>

            <div class="settings-actions">
                <button class="btn btn-primary" onclick="saveSettings()">Save Settings</button>
            </div>
        </div>
    `;
}

async function saveSettings() {
    const activityTypeRates = {};
    KNOWN_ACTIVITY_TYPES.forEach(a => {
        const val = parseFloat(document.getElementById(`rate-${a.type}`)?.value || '');
        if (!isNaN(val) && val > 0) activityTypeRates[a.type] = val;
    });

    const settings = {
        graceStartSeconds:          parseInt(document.getElementById('setting-graceStartSeconds').value,  10),
        graceEndMinutes:            parseInt(document.getElementById('setting-graceEndMinutes').value,    10),
        inactivityThresholdMinutes: parseInt(document.getElementById('setting-inactivityThresholdMinutes').value, 10),
        appendNodeShortcut: document.getElementById('setting-appendNodeShortcut').value.trim() || 'Alt+S',
        nextNodeShortcut:   document.getElementById('setting-nextNodeShortcut').value.trim()   || 'Alt+Shift+Oem7',
        billing: {
            currency:                    document.getElementById('setting-currency').value.trim().toUpperCase() || 'USD',
            defaultUserRatePerHour:       parseFloat(document.getElementById('setting-defaultUserRate').value)       || 0,
            defaultProcessingRatePerHour: parseFloat(document.getElementById('setting-defaultProcessingRate').value) || 0,
            activityTypeRates
        }
    };

    try {
        const res = await fetch(`${API_BASE}/api/settings`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(settings)
        });
        const result = await res.json();
        if (result.success) {
            showNotification('Settings saved. Restart the app for tracking changes to take effect.', 'success');
            renderSettings(settings);
        } else {
            showNotification('Failed to save settings', 'error');
        }
    } catch (e) {
        showNotification('Failed to save settings', 'error');
    }
}

// ── Bridge Health Pill ────────────────────────────────────────────────────────

async function fetchBridgeHealth() {
    try {
        const res = await fetch(`${API_BASE}/api/health`);
        if (!res.ok) { setBridgeHealth('red', 'Cannot reach app'); return; }
        const h = await res.json();
        const color = h.level === 'Green' ? 'green' : h.level === 'Amber' ? 'amber' : 'red';
        setBridgeHealth(color, h.summary);
    } catch {
        setBridgeHealth('red', 'App not reachable');
    }
}

function setBridgeHealth(color, text) {
    const pill = document.getElementById('bridge-health');
    const dot  = pill?.querySelector('.bridge-dot');
    const txt  = document.getElementById('bridge-health-text');
    if (!pill) return;
    pill.dataset.level = color;
    if (dot) dot.style.background = color === 'green' ? '#4ade80' : color === 'amber' ? '#fbbf24' : '#f87171';
    if (txt) txt.textContent = text;

    let link = document.getElementById('bridge-health-troubleshoot');
    if (color === 'red') {
        if (!link) {
            link = document.createElement('a');
            link.id = 'bridge-health-troubleshoot';
            link.href = '#';
            link.textContent = 'Troubleshoot →';
            link.style.cssText = 'margin-left:8px;font-size:0.8em;color:#f87171;text-decoration:underline;';
            link.onclick = e => { e.preventDefault(); switchTab('troubleshooter', document.getElementById('tab-btn-troubleshooter')); };
            pill.appendChild(link);
        }
    } else if (link) {
        link.remove();
    }
}

// ── Troubleshooter Tab ────────────────────────────────────────────────────────

let _diagnosticsResults = [];

async function runDiagnostics() {
    const container = document.getElementById('diagnostics-container');
    container.innerHTML = '<p class="loading">Running diagnostics (this may take a few seconds)…</p>';
    try {
        const res = await fetch(`${API_BASE}/api/diagnostics`);
        _diagnosticsResults = await res.json();
        renderDiagnostics(_diagnosticsResults);
    } catch (e) {
        container.innerHTML = '<p class="loading">Failed to run diagnostics — is the app running?</p>';
    }
}

function renderDiagnostics(results) {
    const container = document.getElementById('diagnostics-container');
    if (!results || results.length === 0) {
        container.innerHTML = '<p class="loading">No results.</p>';
        return;
    }
    container.innerHTML = results.map(r => {
        const icon = r.status === 'Pass' ? '✅' : r.status === 'Warn' ? '⚠️' : r.status === 'Fail' ? '❌' : '⏭';
        const cardClass = r.status === 'Fail' ? 'diag-fail' : r.status === 'Warn' ? 'diag-warn' : r.status === 'Pass' ? 'diag-pass' : 'diag-skip';
        const messagesHtml = (r.messages || []).map(m => {
            const cls = m.severity === 'Fail' ? 'msg-fail' : m.severity === 'Warn' ? 'msg-warn' : 'msg-pass';
            return `<div class="diag-msg ${cls}">${escapeHtml(m.text)}</div>`;
        }).join('');
        const optionsHtml = (r.options || []).length > 0
            ? `<div class="diag-options">${(r.options || []).map((o, i) => `
                <div class="diag-option">
                    <strong>${i + 1}. ${escapeHtml(o.label)}</strong>
                    <pre class="diag-instructions">${escapeHtml(o.instructions)}</pre>
                    <div class="diag-option-btns">
                        <button class="btn btn-sm btn-secondary" onclick="navigator.clipboard.writeText(${JSON.stringify(o.instructions)})">📋 Copy</button>
                        ${o.autoFixId ? `<button class="btn btn-sm btn-primary" onclick="applyFix(${JSON.stringify(o.autoFixId)})">⚡ Apply</button>` : ''}
                    </div>
                </div>`).join('')}</div>`
            : '';
        return `
            <div class="diag-card ${cardClass}">
                <div class="diag-header">
                    <span class="diag-icon">${icon}</span>
                    <span class="diag-title">${escapeHtml(r.title)}</span>
                </div>
                <div class="diag-body">${messagesHtml}${optionsHtml}</div>
            </div>`;
    }).join('');
}

async function applyFix(autoFixId) {
    try {
        const res = await fetch(`${API_BASE}/api/diagnostics/apply-fix/${encodeURIComponent(autoFixId)}`, { method: 'POST' });
        const data = await res.json();
        showNotification(data.message || (data.success ? 'Fix applied' : 'Fix failed'), data.success ? 'success' : 'error');
        if (data.success) await runDiagnostics();
    } catch { showNotification('Failed to apply fix', 'error'); }
}

async function copyReport() {
    try {
        const res = await fetch(`${API_BASE}/api/diagnostics/report`);
        const text = await res.text();
        await navigator.clipboard.writeText(text);
        showNotification('Report copied to clipboard', 'success');
    } catch { showNotification('Failed to copy report', 'error'); }
}

async function saveReport() {
    try {
        const res = await fetch(`${API_BASE}/api/diagnostics/report`);
        const text = await res.text();
        const blob = new Blob([text], { type: 'text/plain' });
        const url  = URL.createObjectURL(blob);
        const a    = document.createElement('a');
        a.href     = url;
        a.download = `davinci-tracker-diag-${new Date().toISOString().slice(0,10)}.txt`;
        a.click();
        URL.revokeObjectURL(url);
    } catch { showNotification('Failed to save report', 'error'); }
}
