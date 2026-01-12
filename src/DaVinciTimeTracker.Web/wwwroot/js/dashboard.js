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
