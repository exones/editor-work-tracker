const API_BASE = "";
let refreshInterval;

async function fetchCurrentStatus() {
    try {
        const response = await fetch(`${API_BASE}/api/current`);
        const data = await response.json();
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
        statusText.textContent = `Currently tracking: "${status.projectName}" [${status.state}]`;
        indicator.classList.add(status.state.toLowerCase());
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

    container.innerHTML = projects
        .map(
            project => `
        <div class="project-card ${project.isCurrentlyTracking ? "currently-tracking" : ""}">
            <div class="project-header">
                <div class="project-name">
                    ${escapeHtml(project.projectName)}
                    ${project.isCurrentlyTracking ? '<span class="tracking-badge">● Tracking</span>' : ""}
                </div>
                <button class="btn-delete" onclick="showDeleteConfirmation('${escapeHtml(project.projectName)}')" title="Delete project">
                    🗑️
                </button>
            </div>
            <div class="project-stats">
                <div class="stat">
                    <span class="stat-label">Total Time</span>
                    <span class="stat-value">${formatTimeSpan(project.totalElapsedTime.totalSeconds)}</span>
                </div>
                <div class="stat">
                    <span class="stat-label">Sessions</span>
                    <span class="stat-value">${project.sessionCount}</span>
                </div>
                <div class="stat">
                    <span class="stat-label">Last Activity</span>
                    <span class="stat-value">${formatLastActivity(project.lastActivity)}</span>
                </div>
            </div>
        </div>
    `,
        )
        .join("");
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

// Initial load
refresh();

// Auto-refresh every 5 seconds
refreshInterval = setInterval(refresh, 5000);
