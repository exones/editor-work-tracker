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
            statusText.textContent = `Currently tracking: "${status.projectName}"`;
            indicator.classList.add("tracking");
        }
    } else {
        statusText.textContent = "Not tracking";
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
                    
                    const pageBreakdownHtml = renderPageBreakdown(user.pageBreakdown);

                    return `
                        <div class="user-stat ${isCurrentUser ? 'current-user' : ''}">
                            <div class="user-info">
                                <span class="user-name">${escapeHtml(user.userName)}</span>
                                ${userBadge}
                                ${trackingBadge}
                            </div>
                            <div class="user-time-info">
                                <span class="user-time">${formatTimeSpan(user.totalElapsedTime.totalSeconds)}</span>
                                <span class="user-sessions">${user.sessionCount} session${user.sessionCount !== 1 ? 's' : ''}</span>
                                <span class="user-last-activity">${formatLastActivity(user.lastActivity)}</span>
                            </div>
                            ${pageBreakdownHtml}
                        </div>
                    `;
                })
                .join("");

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
                    <div class="users-container">
                        ${usersHtml}
                    </div>
                </div>
            `;
        })
        .join("");
}

// ── Page breakdown ────────────────────────────────────────────────────────────

const PAGE_COLORS = {
    color:     "#8b5cf6",   // violet
    edit:      "#3b82f6",   // blue
    cut:       "#06b6d4",   // cyan
    media:     "#64748b",   // slate
    fusion:    "#f97316",   // orange
    fairlight: "#10b981",   // emerald
    deliver:   "#6366f1",   // indigo
    photo:     "#ec4899",   // pink
    unknown:   "#94a3b8",   // gray
};

const PAGE_LABELS = {
    color:     "Color",
    edit:      "Edit",
    cut:       "Cut",
    media:     "Media",
    fusion:    "Fusion",
    fairlight: "Fairlight",
    deliver:   "Deliver",
    photo:     "Photo",
};

function renderPageBreakdown(breakdown) {
    if (!breakdown || breakdown.length === 0) return "";

    const chips = breakdown.map(p => {
        const color = PAGE_COLORS[p.page] || PAGE_COLORS.unknown;
        const label = PAGE_LABELS[p.page] || p.page;
        const time  = formatTimeSpan(p.totalTime.totalSeconds);
        return `<span class="page-chip" style="--chip-color:${color}" title="${label}: ${time} (${p.percentage}%)">
            <span class="page-chip-dot"></span>${label} <span class="page-chip-meta">${time} · ${p.percentage}%</span>
        </span>`;
    }).join("");

    return `<div class="page-chips">${chips}</div>`;
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
    if (name === "toggles") fetchNodeToggles();
}

// ── Initial load ──────────────────────────────────────────────────────────────

// Initial load
refresh();

// Auto-refresh every 5 seconds
refreshInterval = setInterval(refresh, 5000);

// ── Node Toggle Panel ─────────────────────────────────────────────────────────

let editingToggleId = null; // null = creating new

async function fetchNodeToggles() {
    try {
        const response = await fetch(`${API_BASE}/api/node-toggles`);
        const groups = await response.json();
        renderNodeToggles(groups);
    } catch (error) {
        console.error("Failed to fetch node toggles:", error);
        document.getElementById("toggles-container").innerHTML =
            '<p class="loading">Failed to load node toggles.</p>';
    }
}

function renderNodeToggles(groups) {
    const container = document.getElementById("toggles-container");
    if (!groups || groups.length === 0) {
        container.innerHTML = '<p class="no-projects">No toggle groups configured. Click "+ Add Group" to create one.</p>';
        return;
    }

    container.innerHTML = groups.map(g => {
        const stateLabel = g.currentEnabled === true  ? '<span class="toggle-state on">ON</span>'
            : g.currentEnabled === false ? '<span class="toggle-state off">OFF</span>'
            : '<span class="toggle-state unknown" title="Press Test once to sync (first press always disables)">? unknown</span>';

        const nodeList = (g.nodes || []).map(n => {
            const id = n.nodeId != null ? `#${n.nodeId}` : "";
            const title = n.title ? escapeHtml(n.title) : "";
            const label = [id, title].filter(Boolean).join(" ");
            return `<span class="node-chip">${label} <em>(${n.level})</em></span>`;
        }).join(" ");

        return `
        <div class="toggle-card">
            <div class="toggle-header">
                <div class="toggle-info">
                    <span class="toggle-name">${escapeHtml(g.name)}</span>
                    <span class="hotkey-badge">${escapeHtml(g.hotkey || "(no hotkey)")}</span>
                    ${stateLabel}
                </div>
                <div class="toggle-actions">
                    <button class="btn btn-sm btn-test" onclick="executeToggle('${g.id}')" title="Toggle nodes now (alternates ON/OFF)">▶ Test</button>
                    <button class="btn btn-sm btn-sync" onclick="syncState('${g.id}')" title="Tell the app nodes are currently ON (sync after manual DaVinci change)">↺ Sync ON</button>
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

async function executeToggle(id) {
    try {
        const response = await fetch(`${API_BASE}/api/node-toggles/${id}/execute`, { method: "POST" });
        const result = await response.json();
        if (result.success) {
            const state = result.enabled ? "enabled" : "disabled";
            showNotification(`Toggled → ${state}`, "success");
            fetchNodeToggles();
        } else {
            showNotification(result.message || "Toggle failed — check logs", "error");
        }
    } catch (error) {
        showNotification("Toggle request failed", "error");
    }
}

function showToggleModal(group = null) {
    editingToggleId = group ? group.id : null;
    document.getElementById("toggle-modal-title").textContent = group ? "Edit Toggle Group" : "Add Toggle Group";
    document.getElementById("toggle-name").value = group ? group.name : "";
    document.getElementById("toggle-hotkey").value = group ? group.hotkey : "";

    const nodesList = document.getElementById("toggle-nodes-list");
    nodesList.innerHTML = "";
    const nodes = group ? group.nodes : [];
    if (nodes.length === 0) addNodeRow();
    else nodes.forEach(n => addNodeRow(n));

    document.getElementById("toggle-modal").style.display = "flex";
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

    const group = { name, hotkey, nodes };
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
