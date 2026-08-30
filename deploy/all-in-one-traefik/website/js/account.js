function escapeHtml(value) {
    const element = document.createElement('div');
    element.textContent = value == null ? '' : String(value);
    return element.innerHTML;
}

function number(value) {
    return Number(value || 0).toLocaleString('pt-BR');
}

function renderCharacters(characters) {
    const grid = document.getElementById('charactersGrid');
    const message = document.getElementById('accountMessage');
    document.getElementById('summaryCharacters').textContent = `${characters.length} CRIADOS`;

    if (!characters.length) {
        message.textContent = 'Nenhum personagem criado nesta conta.';
        return;
    }

    message.hidden = true;
    grid.innerHTML = characters.map(character => `
        <article class="character-card">
            <div class="character-header">
                <h3 class="character-name">${escapeHtml(character.name)}</h3>
                <span class="character-class-badge">${escapeHtml(character.className || 'Classe não informada')}</span>
            </div>
            <p class="character-guild">Guild: <strong>${escapeHtml(character.guild || 'Sem guild')}</strong><br>
                <span class="${character.status === 'Online' ? 'online-dot' : 'offline-dot'}">&#9679; ${character.status || 'Offline'}</span></p>
            <div class="char-stats-grid">
                <div class="char-stat-item"><span class="char-stat-lbl">Level</span><strong class="char-stat-val">${number(character.level)}</strong></div>
                <div class="char-stat-item"><span class="char-stat-lbl">Resets</span><strong class="char-stat-val">${number(character.resets)}</strong></div>
                <div class="char-stat-item"><span class="char-stat-lbl">Master Level</span><strong class="char-stat-val">${number(character.masterLevel)}</strong></div>
                <div class="char-stat-item"><span class="char-stat-lbl">PK Kills</span><strong class="char-stat-val">${number(character.kills)}</strong></div>
            </div>
        </article>
    `).join('');

    const totals = characters.reduce((result, character) => ({
        resets: result.resets + Number(character.resets || 0),
        kills: result.kills + Number(character.kills || 0),
        level: result.level + Number(character.level || 0)
    }), { resets: 0, kills: 0, level: 0 });
    document.getElementById('statsContent').innerHTML = `
        <div class="stat-overview-card"><span>LEVELS SOMADOS</span><strong>${number(totals.level)}</strong></div>
        <div class="stat-overview-card"><span>RESETS SOMADOS</span><strong>${number(totals.resets)}</strong></div>
        <div class="stat-overview-card"><span>PK KILLS</span><strong>${number(totals.kills)}</strong></div>
    `;
}

async function loadAccount() {
    const response = await fetch('/api/me');
    if (response.status === 401) {
        window.location.href = 'index.html';
        return;
    }
    const data = await response.json();
    if (!response.ok || !data.success) throw new Error(data.message || 'Erro ao carregar painel da conta');

    const account = data.account;
    document.getElementById('accountLogin').textContent = account.loginName;
    document.getElementById('accountEmail').textContent = account.email;
    document.getElementById('accountInitial').textContent = account.loginName.charAt(0).toUpperCase();
    document.getElementById('summaryLogin').textContent = account.loginName.toUpperCase();
    document.getElementById('summaryEmail').textContent = account.email;
    renderCharacters(account.characters || []);
}

document.querySelectorAll('.panel-tab').forEach(tab => {
    tab.addEventListener('click', async () => {
        document.querySelectorAll('.panel-tab').forEach(item => item.classList.remove('active'));
        document.querySelectorAll('.panel-content').forEach(panel => panel.classList.add('hidden-panel'));
        tab.classList.add('active');
        const panelId = `${tab.dataset.panel}Panel`;
        document.getElementById(panelId).classList.remove('hidden-panel');

        // Load eventos.html content when Events tab is clicked
        if (tab.dataset.panel === 'events') {
            await loadEventsPanel();
        }
    });
});

async function loadEventsPanel() {
    const eventsContent = document.getElementById('eventsContent');
    
    // Check if already loaded
    if (eventsContent.dataset.loaded === 'true') {
        return;
    }

    try {
        const response = await fetch('/eventos.html');
        
        if (response.status === 401) {
            // Not admin - show message
            eventsContent.innerHTML = `
                <div class="events-access-denied">
                    <h3>&#128274; Acesso Restrito</h3>
                    <p>O painel de eventos é exclusivo para administradores.</p>
                    <p>Faça login com uma conta de administrador para acessar.</p>
                </div>
            `;
            eventsContent.dataset.loaded = 'true';
            return;
        }

        if (!response.ok) {
            throw new Error(`Erro ao carregar painel de eventos: ${response.status}`);
        }

        const html = await response.text();
        
        // Extract the main content from eventos.html (everything inside <main> or the body content)
        const parser = new DOMParser();
        const doc = parser.parseFromString(html, 'text/html');
        
        // Get the main content - eventos.html has a <main> tag with the panel content
        const mainContent = doc.querySelector('main');
        const contentToInject = mainContent ? mainContent.innerHTML : doc.body.innerHTML;
        
        eventsContent.innerHTML = contentToInject;
        eventsContent.dataset.loaded = 'true';

        // Load eventos.css if not already loaded
        if (!document.querySelector('link[href*="eventos.css"]')) {
            const link = document.createElement('link');
            link.rel = 'stylesheet';
            link.href = '/css/eventos.css';
            document.head.appendChild(link);
        }

        // Load eventos.js and manually initialize after load.
        await new Promise((resolve, reject) => {
            const script = document.createElement('script');
            script.src = '/js/eventos.js';
            script.onload = resolve;
            script.onerror = reject;
            document.head.appendChild(script);
        });

        // Initialize the events panel. eventos.js exposes initEventsPanel()
        // which works whether the script loads before or after DOMContentLoaded.
        if (typeof initEventsPanel === 'function') {
            initEventsPanel();
        } else if (typeof renderEventsGrid === 'function') {
            renderEventsGrid();
        }
    } catch (error) {
        eventsContent.innerHTML = `
            <div class="events-error">
                <h3>&#9888;&#65039; Erro ao carregar eventos</h3>
                <p>Não foi possível carregar o painel de eventos.</p>
                <p>${escapeHtml(error.message)}</p>
                <button class="btn-retry" onclick="loadEventsPanel()">Tentar Novamente</button>
            </div>
        `;
    }
}

document.getElementById('logoutBtn').addEventListener('click', async () => {
    await fetch('/api/logout', { method: 'POST' });
    window.location.href = 'index.html';
});

loadAccount()
    .then(() => loadEventsPanel())
    .catch(error => {
        document.getElementById('accountMessage').textContent = error.message;
    });
