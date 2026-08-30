/**
 * ==========================================================
 * PAINEL DE EVENTOS - MU FREE SEASON 6 EPISÓDIO 3
 * JS DEDICADO PARA EVENTOS.HTML
 * ==========================================================
 */

// Default Classic MU Events definition
const DEFAULT_SERVER_EVENTS = [
  {
    id: 'evt-bc',
    name: 'Blood Castle',
    category: 'events',
    icon: '🩸',
    colorTheme: '#f43f5e',
    location: 'Lorencia / Devias (NPC Espírito do Arcanjo)',
    frequency: 'A cada 2 horas (00:00, 02:00, 04:00, 06:00...)',
    startTimeStr: '00:00',
    startIntervalMin: 120,
    startOffsetMin: 0,
    durationMin: 15,
    rewardTag: 'Arma do Arcanjo, Chaos Weapon & XP Bônus',
    description: 'Infiltre-se no castelo do Arcanjo, destrua o portão, a estátua de cristal e entregue a arma sagrada.',
    enabled: true
  },
  {
    id: 'evt-ds',
    name: 'Devil Square',
    category: 'events',
    icon: '😈',
    colorTheme: '#a855f7',
    location: 'Noria (NPC Charon)',
    frequency: 'A cada 2 horas (00:30, 02:30, 04:30, 06:30...)',
    startTimeStr: '00:30',
    startIntervalMin: 120,
    startOffsetMin: 30,
    durationMin: 20,
    rewardTag: 'Alta Densidade de XP & Joias Raras',
    description: 'Sobreviva a ondas contínuas de monstros demoníacos e dispute a liderança de pontuação.',
    enabled: true
  },
  {
    id: 'evt-cc',
    name: 'Chaos Castle',
    category: 'events',
    icon: '🏰',
    colorTheme: '#eab308',
    location: 'Lorencia / Devias (NPC Guarda do Castelo)',
    frequency: 'A cada 2 horas (01:00, 03:00, 05:00, 07:00...)',
    startTimeStr: '01:00',
    startIntervalMin: 120,
    startOffsetMin: 60,
    durationMin: 15,
    rewardTag: 'Ancient Items, Joias & Pacotes de Criação',
    description: 'Battle Royale mortal onde todos vestem a mesma armadura. O último sobrevivente leva o tesouro ancient.',
    enabled: true
  },
  {
    id: 'evt-dragon',
    name: 'Invasão de Dragões Vermelhos',
    category: 'invasions',
    icon: '🐲',
    colorTheme: '#f97316',
    location: 'Lorencia, Noria e Devias',
    frequency: 'A cada 4 horas (00:00, 04:00, 08:00, 12:00...)',
    startTimeStr: '00:00',
    startIntervalMin: 240,
    startOffsetMin: 0,
    durationMin: 10,
    rewardTag: 'Box of Kundun +1 a +3 & Jewel of Bless',
    description: 'Dragões sobrevoam os mapas iniciais atacando aventureiros com baforadas de fogo.',
    enabled: true
  },
  {
    id: 'evt-golden',
    name: 'Invasão Golden Troops',
    category: 'invasions',
    icon: '⚔️',
    colorTheme: '#d4af37',
    location: 'Lorencia, Devias, Noria, LostTower, Atlans e Tarkan',
    frequency: 'A cada 4 horas (01:00, 05:09, 09:00, 13:00...)',
    startTimeStr: '01:00',
    startIntervalMin: 240,
    startOffsetMin: 60,
    durationMin: 15,
    rewardTag: 'Box of Kundun +1 a +5 (Equipamentos Excelentes)',
    description: 'Monstros dourados espalham-se por todo o continente com drops valiosos.',
    enabled: true
  },
  {
    id: 'evt-kundun',
    name: 'Kalima 7: Lord Kundun Boss',
    category: 'invasions',
    icon: '👑',
    colorTheme: '#ec4899',
    location: 'Kalima 7 (Fim do Mapa)',
    frequency: 'A cada 12 horas (04:00, 16:00)',
    startTimeStr: '04:00',
    startIntervalMin: 720,
    startOffsetMin: 240,
    durationMin: 60,
    rewardTag: 'Ancient Items Top Tier & 380 Weapons',
    description: 'O mestre das trevas renasce nas profundezas de Kalima para desafiar as guildas mais poderosas.',
    enabled: true
  },
  {
    id: 'evt-medusa',
    name: 'Invasão da Rainha Medusa',
    category: 'invasions',
    icon: '🐍',
    colorTheme: '#10b981',
    location: 'Peace Swamp (Pântano da Paz)',
    frequency: 'A cada 12 horas (08:00, 20:00)',
    startTimeStr: '08:00',
    startIntervalMin: 720,
    startOffsetMin: 480,
    durationMin: 45,
    rewardTag: 'Socket Items, Joias Especiais & Sphere',
    description: 'A rainha de serpentes domina o pântano petrificando qualquer guerreiro despreparado.',
    enabled: true
  },
  {
    id: 'evt-cs',
    name: 'Castle Siege (Guerra dos Castelos)',
    category: 'events',
    icon: '👑',
    colorTheme: '#38bdf8',
    location: 'Valley of Loren (Castelo Principal)',
    frequency: 'Domingo às 20:00 (Semanal)',
    startTimeStr: '20:00',
    startIntervalMin: 1440,
    startOffsetMin: 1200,
    durationMin: 120,
    rewardTag: 'Domínio de Land of Trials, Taxas e Coroa do Castelo',
    description: 'A maior batalha PVP de guildas do continente pelo controle supremo do trono.',
    enabled: true
  }
];

const STORAGE_KEY = 'mu_server_events_config';

// State variables
let serverEvents = [];
let currentCategoryFilter = 'all';
let currentSearchQuery = '';
let editingEventId = null;
let modalCurrentStartTime = '00:00';
let modalCurrentInterval = 120;

// Helper: parse "HH:mm" to minutes of day
function parseTimeToMinutes(timeStr) {
  if (!timeStr) return 0;
  const parts = timeStr.split(':');
  if (parts.length < 2) return 0;
  const h = parseInt(parts[0], 10) || 0;
  const m = parseInt(parts[1], 10) || 0;
  return Math.min(1439, Math.max(0, h * 60 + m));
}

// Helper: format minutes of day to "HH:mm"
function formatMinutesToTime(totalMin) {
  const m = ((Math.floor(totalMin) % 1440) + 1440) % 1440;
  const hours = Math.floor(m / 60);
  const mins = m % 60;
  return `${String(hours).padStart(2, '0')}:${String(mins).padStart(2, '0')}`;
}

// Helper: compute all daily occurrence times
function computeDailySchedule(startTimeStr, intervalMin) {
  const startMin = parseTimeToMinutes(startTimeStr);
  const interval = Math.max(10, intervalMin || 120);
  const times = [];
  
  if (interval >= 1440) {
    return [formatMinutesToTime(startMin)];
  }

  for (let m = startMin; m < 1440; m += interval) {
    times.push(formatMinutesToTime(m));
  }
  
  for (let m = startMin - interval; m >= 0; m -= interval) {
    times.unshift(formatMinutesToTime(m));
  }

  const unique = Array.from(new Set(times)).sort((a, b) => parseTimeToMinutes(a) - parseTimeToMinutes(b));
  return unique;
}

// Helper: compute dynamic status for an event at current moment
function computeEventStatus(event, now = new Date()) {
  const currentMinuteOfDay = now.getHours() * 60 + now.getMinutes() + now.getSeconds() / 60;
  const startTime = event.startTimeStr || (event.startOffsetMin !== undefined ? formatMinutesToTime(event.startOffsetMin) : '00:00');
  const startOffset = parseTimeToMinutes(startTime);
  const intervalMin = Math.max(1, event.startIntervalMin || 120);
  const durationMin = Math.max(1, event.durationMin || 15);

  let minutesSinceLastStart = (currentMinuteOfDay - startOffset) % intervalMin;
  if (minutesSinceLastStart < 0) {
    minutesSinceLastStart += intervalMin;
  }

  const secondsSinceLastStart = minutesSinceLastStart * 60;
  const durationSec = durationMin * 60;
  const intervalSec = intervalMin * 60;

  let isOpen = false;
  let isImminent = false;
  let statusLabel = '';
  let countdownFormatted = '';
  let progressPct = 0;

  if (secondsSinceLastStart < durationSec) {
    isOpen = true;
    const remainingOpenSec = Math.floor(durationSec - secondsSinceLastStart);
    const m = Math.floor(remainingOpenSec / 60);
    const s = remainingOpenSec % 60;
    statusLabel = 'ABERTO AGORA';
    countdownFormatted = `Fecha em ${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
    progressPct = 100 - Math.floor((remainingOpenSec / durationSec) * 100);
  } else {
    isOpen = false;
    const remainingSec = Math.floor(intervalSec - secondsSinceLastStart);
    const h = Math.floor(remainingSec / 3600);
    const m = Math.floor((remainingSec % 3600) / 60);
    const s = remainingSec % 60;

    if (h > 0) {
      countdownFormatted = `${String(h).padStart(2, '0')}h ${String(m).padStart(2, '0')}m ${String(s).padStart(2, '0')}s`;
    } else {
      countdownFormatted = `${String(m).padStart(2, '0')}m ${String(s).padStart(2, '0')}s`;
    }

    if (remainingSec <= 300) {
      isImminent = true;
      statusLabel = 'INICIANDO!';
    } else {
      statusLabel = `Em ${h > 0 ? h + 'h ' : ''}${m}m`;
    }

    progressPct = Math.min(100, Math.max(0, Math.floor((secondsSinceLastStart / intervalSec) * 100)));
  }

  const scheduledTimes = computeDailySchedule(startTime, intervalMin);
  const nextOccMin = (Math.floor((currentMinuteOfDay - startOffset) / intervalMin) + 1) * intervalMin + startOffset;
  const nextOccurrenceTime = formatMinutesToTime(nextOccMin % 1440);

  return {
    isOpen,
    isImminent,
    statusLabel,
    countdownFormatted,
    progressPct,
    scheduledTimes,
    nextOccurrenceTime
  };
}

// Load events from LocalStorage
function loadEvents() {
  const saved = localStorage.getItem(STORAGE_KEY);
  if (saved) {
    try {
      const parsed = JSON.parse(saved);
      if (Array.isArray(parsed) && parsed.length > 0) {
        serverEvents = parsed.map(e => ({
          ...e,
          startTimeStr: e.startTimeStr || (e.startOffsetMin !== undefined ? formatMinutesToTime(e.startOffsetMin) : '00:00'),
          startIntervalMin: Number(e.startIntervalMin) || 120,
          durationMin: Number(e.durationMin) || 15
        }));
        return;
      }
    } catch (err) {
      console.warn('Erro ao carregar eventos:', err);
    }
  }
  serverEvents = JSON.parse(JSON.stringify(DEFAULT_SERVER_EVENTS));
  saveEvents();
}

// Save events to LocalStorage & dispatch cross-window event
function saveEvents() {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(serverEvents));
  window.dispatchEvent(new Event('storage'));
}

// Format date time for server clock
function updateClock() {
  const now = new Date();
  const timeStr = now.toLocaleTimeString('pt-BR');
  const clockEl = document.getElementById('serverClock');
  if (clockEl) {
    clockEl.innerText = timeStr;
  }
}

// Render the Stats Counters
function renderStats(now = new Date()) {
  const total = serverEvents.length;
  const active = serverEvents.filter(e => e.enabled !== false).length;
  
  let openNow = 0;
  let imminent = 0;

  serverEvents.filter(e => e.enabled !== false).forEach(e => {
    const status = computeEventStatus(e, now);
    if (status.isOpen) openNow++;
    else if (status.isImminent) imminent++;
  });

  const statTotal = document.getElementById('statTotal');
  const statActive = document.getElementById('statActive');
  const statOpen = document.getElementById('statOpen');
  const statImminent = document.getElementById('statImminent');

  if (statTotal) statTotal.innerText = total;
  if (statActive) statActive.innerText = active;
  if (statOpen) statOpen.innerText = openNow;
  if (statImminent) statImminent.innerText = imminent;
}

// Render Event Cards Grid
function renderEventsGrid() {
  const now = new Date();
  const container = document.getElementById('eventsCardsGrid');
  const emptyState = document.getElementById('emptyEventsState');
  if (!container) return;

  renderStats(now);

  const query = currentSearchQuery.toLowerCase().trim();
  const filtered = serverEvents.filter(e => {
    if (currentCategoryFilter === 'events' && e.category !== 'events') return false;
    if (currentCategoryFilter === 'invasions' && e.category !== 'invasions') return false;
    
    const status = computeEventStatus(e, now);
    if (currentCategoryFilter === 'active') {
      if (e.enabled === false) return false;
      if (!status.isOpen && !status.isImminent) return false;
    }

    if (query) {
      const matchName = (e.name || '').toLowerCase().includes(query);
      const matchLoc = (e.location || '').toLowerCase().includes(query);
      const matchReward = (e.rewardTag || '').toLowerCase().includes(query);
      if (!matchName && !matchLoc && !matchReward) return false;
    }

    return true;
  });

  if (filtered.length === 0) {
    container.style.display = 'none';
    if (emptyState) {
      emptyState.style.display = 'block';
      const emptyMsg = document.getElementById('emptyFilterName');
      if (emptyMsg) emptyMsg.innerText = currentSearchQuery || currentCategoryFilter;
    }
    return;
  }

  if (emptyState) emptyState.style.display = 'none';
  container.style.display = 'grid';

  let html = '';
  filtered.forEach(ev => {
    const status = computeEventStatus(ev, now);
    const startTimeStr = ev.startTimeStr || formatMinutesToTime(ev.startOffsetMin || 0);
    const isCardOpen = status.isOpen;
    const isCardImminent = status.isImminent;
    const isCardDisabled = ev.enabled === false;

    let cardClass = 'event-card';
    if (isCardOpen) cardClass += ' event-card-open';
    else if (isCardImminent) cardClass += ' event-card-imminent';
    if (isCardDisabled) cardClass += ' event-card-disabled';

    let statusPillHtml = '';
    if (isCardOpen) {
      statusPillHtml = `<span class="event-status-pill open"><span class="status-pulse-dot"></span> ABERTO AGORA</span>`;
    } else if (isCardImminent) {
      statusPillHtml = `<span class="event-status-pill imminent"><span class="status-pulse-dot" style="background:#f59e0b"></span> ${status.countdownFormatted}</span>`;
    } else {
      statusPillHtml = `<span class="event-status-pill upcoming">${status.countdownFormatted}</span>`;
    }

    // Daily chips
    const timesChips = status.scheduledTimes.map(t => {
      const isNext = t === status.nextOccurrenceTime;
      const chipStyle = isNext
        ? 'background:rgba(212,175,55,0.25); border-color:#d4af37; color:#fef08a;'
        : 'background:rgba(0,0,0,0.5); border-color:rgba(255,255,255,0.08); color:#94a3b8;';
      return `<span style="display:inline-block; padding:2px 6px; border-radius:4px; font-family:monospace; font-size:10px; font-weight:bold; border:1px solid transparent; ${chipStyle}">${t}</span>`;
    }).join(' ');

    html += `
      <div class="${cardClass}" id="card-${ev.id}">
        <div>
          <!-- Header -->
          <div class="event-card-header mb-3">
            <div style="display:flex; align-items:center; gap:12px;">
              <div class="event-icon-badge" style="background:${ev.colorTheme}15; border-color:${ev.colorTheme}40; color:${ev.colorTheme};">
                ${ev.icon}
              </div>
              <div>
                <div style="display:flex; align-items:center; gap:8px;">
                  <h3 class="event-info-title">${ev.name}</h3>
                  ${isCardDisabled ? '<span style="font-size:9px; font-family:monospace; text-transform:uppercase; padding:2px 6px; border-radius:3px; background:rgba(239,68,68,0.2); border:1px solid rgba(239,68,68,0.4); color:#fca5a5;">Desativado</span>' : ''}
                </div>
                <div class="event-info-category" style="background:${ev.category === 'invasions' ? 'rgba(239,68,68,0.15)' : 'rgba(56,189,248,0.15)'}; border:1px solid ${ev.category === 'invasions' ? 'rgba(239,68,68,0.3)' : 'rgba(56,189,248,0.3)'}; color:${ev.category === 'invasions' ? '#fca5a5' : '#7dd3fc'};">
                  ${ev.category === 'invasions' ? '🐲 Invasão de Monstros' : '⚔️ Evento Clássico'}
                </div>
              </div>
            </div>
            ${statusPillHtml}
          </div>

          <!-- Specs Matrix -->
          <div class="event-specs-grid mb-3">
            <div class="event-spec-item">
              <span class="event-spec-label">Horário Inicial</span>
              <span class="event-spec-val" style="font-family:monospace; color:#fef08a;">${startTimeStr}</span>
            </div>
            <div class="event-spec-item">
              <span class="event-spec-label">Intervalo</span>
              <span class="event-spec-val" style="font-family:monospace; color:#fed7aa;">A cada ${ev.startIntervalMin}m</span>
            </div>
            <div class="event-spec-item">
              <span class="event-spec-label">Duração Portão</span>
              <span class="event-spec-val" style="color:#6ee7b7;">${ev.durationMin} min</span>
            </div>
            <div class="event-spec-item">
              <span class="event-spec-label">Localização</span>
              <span class="event-spec-val" style="font-size:12px; color:#cbd5e1;" title="${ev.location}">${ev.location}</span>
            </div>
          </div>

          <!-- Daily Schedule Chips -->
          <div style="background:rgba(0,0,0,0.5); border:1px solid rgba(212,175,55,0.15); border-radius:6px; padding:8px; margin-bottom:8px;">
            <div style="display:flex; justify-content:space-between; font-size:10px; color:#94a3b8; font-family:monospace; margin-bottom:4px;">
              <span style="color:#d4af37; font-weight:bold; text-transform:uppercase;">Horários Diários:</span>
              ${status.nextOccurrenceTime ? `<span style="color:#fef08a;">Próximo: <strong>${status.nextOccurrenceTime}</strong></span>` : ''}
            </div>
            <div style="display:flex; flex-wrap:wrap; gap:4px;">
              ${timesChips}
            </div>
          </div>

          <!-- Rewards & Drops -->
          <div style="background:rgba(0,0,0,0.4); border:1px solid rgba(212,175,55,0.15); border-radius:6px; padding:8px; margin-bottom:8px;">
            <span class="event-spec-label" style="display:block; color:#d4af37; font-weight:bold; margin-bottom:2px;">🎁 Drops & Premiações</span>
            <p style="font-size:12px; color:#fde047; margin:0; line-height:1.3;">${ev.rewardTag}</p>
          </div>

          <!-- Description if exists -->
          ${ev.description ? `<p style="font-size:11px; color:#94a3b8; font-style:italic; margin-bottom:8px; line-height:1.3;">${ev.description}</p>` : ''}
        </div>

        <div>
          <!-- Progress Bar -->
          <div style="margin-bottom:10px;">
            <div style="display:flex; justify-content:space-between; font-size:10px; color:#94a3b8; font-family:monospace; margin-bottom:2px;">
              <span>Progresso do Ciclo</span>
              <span style="color:#fde047;">${status.progressPct}%</span>
            </div>
            <div class="event-progress-track">
              <div class="event-progress-fill" style="width:${status.progressPct}%; background:${isCardOpen ? '#10b981' : (isCardImminent ? '#f59e0b' : 'linear-gradient(90deg, #d4af37, #fde047)')};"></div>
            </div>
          </div>

          <!-- Card Action Buttons -->
          <div class="event-card-actions">
            <div style="display:flex; align-items:center; gap:8px;">
              <button type="button" class="event-btn-action" onclick="openEditModal('${ev.id}')" title="Configurar este evento">
                ✏️ Editar
              </button>
              <button type="button" class="event-btn-action event-btn-test" onclick="forceStartEvent('${ev.id}')" title="Abrir portões agora (Teste rápido)">
                ⚡ Abrir Agora
              </button>
              <button type="button" class="event-btn-action event-btn-delete" onclick="deleteEvent('${ev.id}')" title="Excluir este evento">
                🗑️
              </button>
            </div>

            <!-- Toggle Switch -->
            <div style="display:flex; align-items:center; gap:6px;">
              <span style="font-size:11px; color:#94a3b8;">${ev.enabled !== false ? 'Ativo' : 'Pausado'}</span>
              <label class="switch-toggle" title="${ev.enabled !== false ? 'Desativar evento' : 'Ativar evento'}">
                <input type="checkbox" ${ev.enabled !== false ? 'checked' : ''} onchange="toggleEventEnabled('${ev.id}')">
                <span class="switch-slider"></span>
              </label>
            </div>
          </div>
        </div>
      </div>
    `;
  });

  container.innerHTML = html;
}

// Toggle enabled status of event
function toggleEventEnabled(id) {
  serverEvents = serverEvents.map(e => {
    if (e.id === id) {
      return { ...e, enabled: e.enabled === false ? true : false };
    }
    return e;
  });
  saveEvents();
  renderEventsGrid();
}

// Force start event (aligns startTime to current hour/minute so gates open immediately)
function forceStartEvent(id) {
  const now = new Date();
  const currentHHMM = formatMinutesToTime(now.getHours() * 60 + now.getMinutes());
  
  serverEvents = serverEvents.map(e => {
    if (e.id === id) {
      return {
        ...e,
        startTimeStr: currentHHMM,
        startOffsetMin: parseTimeToMinutes(currentHHMM),
        enabled: true
      };
    }
    return e;
  });
  saveEvents();
  renderEventsGrid();
  showToast('⚡ Portões abertos com sucesso para teste ao vivo!');
}

// Delete event with confirmation
function deleteEvent(id) {
  const target = serverEvents.find(e => e.id === id);
  if (!target) return;
  
  if (confirm(`Tem certeza que deseja excluir o evento "${target.name}"?`)) {
    serverEvents = serverEvents.filter(e => e.id !== id);
    saveEvents();
    renderEventsGrid();
    showToast(`🗑️ Evento "${target.name}" removido.`);
  }
}

// Reset all events to defaults
function resetToDefaults() {
  if (confirm('Restaurar todos os eventos e horários para as configurações padrão originais?')) {
    serverEvents = JSON.parse(JSON.stringify(DEFAULT_SERVER_EVENTS));
    saveEvents();
    renderEventsGrid();
    showToast('🔄 Configurações de eventos restauradas para os padrões.');
  }
}

// Toast notification helper
function showToast(message) {
  const toast = document.getElementById('eventToast');
  if (!toast) return;
  toast.innerText = message;
  toast.style.display = 'block';
  toast.style.opacity = '1';
  setTimeout(() => {
    toast.style.opacity = '0';
    setTimeout(() => { toast.style.display = 'none'; }, 300);
  }, 3000);
}

// Modal open/close & preset handlers
function openCreateModal() {
  editingEventId = null;
  document.getElementById('modalTitle').innerText = 'CRIAR NOVO EVENTO OU INVASÃO';
  document.getElementById('formEventId').value = '';
  document.getElementById('formEventName').value = '';
  document.getElementById('formEventCategory').value = 'events';
  document.getElementById('formEventIcon').value = '⚔️';
  document.getElementById('formEventColor').value = '#d4af37';
  document.getElementById('formEventColorText').innerText = '#D4AF37';
  document.getElementById('formEventLocation').value = 'Lorencia (NPC Central)';
  document.getElementById('formStartTime').value = '00:00';
  document.getElementById('formStartInterval').value = '240';
  document.getElementById('formDuration').value = '15';
  document.getElementById('formRewardTag').value = 'Joias, XP Bônus & Box of Kundun';
  document.getElementById('formDescription').value = 'Evento especial com cronograma configurado.';
  document.getElementById('formEnabled').checked = true;

  modalCurrentStartTime = '00:00';
  modalCurrentInterval = 240;
  updateModalSchedulePreview();

  const backdrop = document.getElementById('eventModalBackdrop');
  if (backdrop) backdrop.classList.add('open');
}

function openEditModal(id) {
  const ev = serverEvents.find(e => e.id === id);
  if (!ev) return;

  editingEventId = id;
  const startTime = ev.startTimeStr || formatMinutesToTime(ev.startOffsetMin || 0);
  const interval = Number(ev.startIntervalMin) || 120;

  document.getElementById('modalTitle').innerText = `EDITAR: ${ev.name.toUpperCase()}`;
  document.getElementById('formEventId').value = ev.id;
  document.getElementById('formEventName').value = ev.name;
  document.getElementById('formEventCategory').value = ev.category || 'events';
  document.getElementById('formEventIcon').value = ev.icon || '⚔️';
  document.getElementById('formEventColor').value = ev.colorTheme || '#d4af37';
  document.getElementById('formEventColorText').innerText = (ev.colorTheme || '#d4af37').toUpperCase();
  document.getElementById('formEventLocation').value = ev.location || '';
  document.getElementById('formStartTime').value = startTime;
  document.getElementById('formStartInterval').value = interval;
  document.getElementById('formDuration').value = ev.durationMin || 15;
  document.getElementById('formRewardTag').value = ev.rewardTag || '';
  document.getElementById('formDescription').value = ev.description || '';
  document.getElementById('formEnabled').checked = ev.enabled !== false;

  modalCurrentStartTime = startTime;
  modalCurrentInterval = interval;
  updateModalSchedulePreview();

  const backdrop = document.getElementById('eventModalBackdrop');
  if (backdrop) backdrop.classList.add('open');
}

function closeEventModal() {
  const backdrop = document.getElementById('eventModalBackdrop');
  if (backdrop) backdrop.classList.remove('open');
}

function updateModalSchedulePreview() {
  const times = computeDailySchedule(modalCurrentStartTime, modalCurrentInterval);
  const container = document.getElementById('modalScheduleChips');
  const countEl = document.getElementById('modalScheduleCount');
  const summaryEl = document.getElementById('modalScheduleSummary');

  if (countEl) countEl.innerText = `(${times.length} ocorrências)`;
  if (summaryEl) summaryEl.innerText = `Inicial: ${modalCurrentStartTime} | A cada ${modalCurrentInterval}m`;

  if (container) {
    container.innerHTML = times.map(t => `
      <span style="display:inline-block; padding:3px 8px; border-radius:4px; font-family:monospace; font-size:11px; font-weight:bold; background:rgba(212,175,55,0.15); border:1px solid rgba(212,175,55,0.3); color:#fef08a;">
        ${t}
      </span>
    `).join(' ');
  }

  // Update frequency text preview
  const hours = modalCurrentInterval / 60;
  let freqLabel = '';
  if (modalCurrentInterval === 1440) {
    freqLabel = `Diário às ${modalCurrentStartTime}`;
  } else if (Number.isInteger(hours) && hours >= 1) {
    const sample = times.slice(0, 4).join(', ') + (times.length > 4 ? '...' : '');
    freqLabel = `A cada ${hours}h (${sample})`;
  } else {
    const sample = times.slice(0, 4).join(', ') + (times.length > 4 ? '...' : '');
    freqLabel = `A cada ${modalCurrentInterval}m (${sample})`;
  }
  const freqPreviewEl = document.getElementById('modalFrequencyPreview');
  if (freqPreviewEl) freqPreviewEl.innerText = freqLabel;
}

// Preset selection helpers
function selectIconPreset(icon) {
  document.getElementById('formEventIcon').value = icon;
}

function selectColorPreset(color) {
  document.getElementById('formEventColor').value = color;
  document.getElementById('formEventColorText').innerText = color.toUpperCase();
}

function selectStartTimePreset(timeStr) {
  document.getElementById('formStartTime').value = timeStr;
  modalCurrentStartTime = timeStr;
  updateModalSchedulePreview();
}

function selectIntervalPreset(min) {
  document.getElementById('formStartInterval').value = min;
  modalCurrentInterval = min;
  updateModalSchedulePreview();
}

function selectDurationPreset(min) {
  document.getElementById('formDuration').value = min;
}

// Save modal event form
function handleEventFormSubmit(e) {
  e.preventDefault();

  const name = document.getElementById('formEventName').value.trim();
  const location = document.getElementById('formEventLocation').value.trim();
  const rewardTag = document.getElementById('formRewardTag').value.trim();
  if (!name || !location || !rewardTag) {
    alert('Por favor, preencha todos os campos obrigatórios (*)');
    return;
  }

  const category = document.getElementById('formEventCategory').value;
  const icon = document.getElementById('formEventIcon').value;
  const colorTheme = document.getElementById('formEventColor').value;
  const startTimeStr = document.getElementById('formStartTime').value || '00:00';
  const startIntervalMin = parseInt(document.getElementById('formStartInterval').value, 10) || 120;
  const durationMin = parseInt(document.getElementById('formDuration').value, 10) || 15;
  const description = document.getElementById('formDescription').value.trim();
  const enabled = document.getElementById('formEnabled').checked;

  const startOffsetMin = parseTimeToMinutes(startTimeStr);
  const calculatedTimes = computeDailySchedule(startTimeStr, startIntervalMin);

  const hours = startIntervalMin / 60;
  let frequency = '';
  if (startIntervalMin === 1440) {
    frequency = `Diário às ${startTimeStr}`;
  } else if (Number.isInteger(hours) && hours >= 1) {
    const sample = calculatedTimes.slice(0, 4).join(', ') + (calculatedTimes.length > 4 ? '...' : '');
    frequency = `A cada ${hours}h (${sample})`;
  } else {
    const sample = calculatedTimes.slice(0, 4).join(', ') + (calculatedTimes.length > 4 ? '...' : '');
    frequency = `A cada ${startIntervalMin}m (${sample})`;
  }

  if (editingEventId) {
    serverEvents = serverEvents.map(item => {
      if (item.id === editingEventId) {
        return {
          ...item,
          name,
          category,
          icon,
          colorTheme,
          location,
          frequency,
          startTimeStr,
          startIntervalMin,
          startOffsetMin,
          durationMin,
          rewardTag,
          description,
          enabled
        };
      }
      return item;
    });
    showToast(`✅ Evento "${name}" atualizado e sincronizado!`);
  } else {
    const newId = 'evt-custom-' + Date.now();
    const newEvent = {
      id: newId,
      name,
      category,
      icon,
      colorTheme,
      location,
      frequency,
      startTimeStr,
      startIntervalMin,
      startOffsetMin,
      durationMin,
      rewardTag,
      description,
      enabled
    };
    serverEvents.push(newEvent);
    showToast(`🎉 Novo evento "${name}" criado com sucesso!`);
  }

  saveEvents();
  closeEventModal();
  renderEventsGrid();
}

// Global initialization
let eventsPanelInitialized = false;
function initEventsPanel() {
  if (eventsPanelInitialized) return;
  eventsPanelInitialized = true;

  loadEvents();
  updateClock();
  renderEventsGrid();

  // 1-second dynamic tick
  setInterval(() => {
    updateClock();
    renderEventsGrid();
  }, 1000);

  // Search input handler
  const searchInput = document.getElementById('eventSearchInput');
  if (searchInput) {
    searchInput.addEventListener('input', (e) => {
      currentSearchQuery = e.target.value;
      renderEventsGrid();
    });
  }

  // Filter tabs handler
  const filterTabs = document.querySelectorAll('.events-filter-tab');
  filterTabs.forEach(tab => {
    tab.addEventListener('click', (e) => {
      filterTabs.forEach(t => t.classList.remove('active'));
      tab.classList.add('active');
      currentCategoryFilter = tab.getAttribute('data-filter') || 'all';
      renderEventsGrid();
    });
  });

  // Modal form event listener
  const form = document.getElementById('eventConfigForm');
  if (form) {
    form.addEventListener('submit', handleEventFormSubmit);
  }

  // Modal Start Time and Interval inputs
  const startTimeInput = document.getElementById('formStartTime');
  if (startTimeInput) {
    startTimeInput.addEventListener('input', (e) => {
      modalCurrentStartTime = e.target.value || '00:00';
      updateModalSchedulePreview();
    });
  }

  const intervalInput = document.getElementById('formStartInterval');
  if (intervalInput) {
    intervalInput.addEventListener('input', (e) => {
      modalCurrentInterval = parseInt(e.target.value, 10) || 120;
      updateModalSchedulePreview();
    });
  }

  const colorInput = document.getElementById('formEventColor');
  if (colorInput) {
    colorInput.addEventListener('input', (e) => {
      document.getElementById('formEventColorText').innerText = e.target.value.toUpperCase();
    });
  }

  // Listen for storage events across tabs
  window.addEventListener('storage', (e) => {
    if (e.key === STORAGE_KEY) {
      loadEvents();
      renderEventsGrid();
    }
  });
}

// Run initialization whether the script loads before or after DOMContentLoaded
// (account.js injects this script dynamically, so DOMContentLoaded may have already fired).
if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', initEventsPanel);
} else {
  initEventsPanel();
}
