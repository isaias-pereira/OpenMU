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
    tab.addEventListener('click', () => {
        document.querySelectorAll('.panel-tab').forEach(item => item.classList.remove('active'));
        document.querySelectorAll('.panel-content').forEach(panel => panel.classList.add('hidden-panel'));
        tab.classList.add('active');
        document.getElementById(`${tab.dataset.panel}Panel`).classList.remove('hidden-panel');
    });
});

document.getElementById('logoutBtn').addEventListener('click', async () => {
    await fetch('/api/logout', { method: 'POST' });
    window.location.href = 'index.html';
});

loadAccount().catch(error => {
    document.getElementById('accountMessage').textContent = error.message;
});
