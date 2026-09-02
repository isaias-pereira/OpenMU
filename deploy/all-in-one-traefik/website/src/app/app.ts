import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  OnDestroy,
  computed,
  inject,
  signal,
  PLATFORM_ID,
} from '@angular/core';
import { isPlatformBrowser, DecimalPipe } from '@angular/common';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';

interface Character {
  id: string;
  name: string;
  className: string;
  classCode: string;
  guild: string;
  level: number;
  masterLevel: number;
  kills: number;
  resets: number;
  masterResets: number;
  strength: number;
  agility: number;
  vitality: number;
  energy: number;
  command?: number;
  status: 'Online' | 'Offline';
}

interface AccountData {
  loginName: string;
  email: string;
  securityCode?: string;
  password?: string;
  role?: 'PLAYER' | 'GM' | 'ADMIN';
  isAdmin?: boolean;
  characters: Character[];
}

interface RankingPlayer {
  position: number;
  characterName: string;
  guildName: string;
  level?: number;
  kills?: number;
  className: string;
  resets?: number;
  masterResets?: number;
}

interface RankingGuild {
  position: number;
  guildName: string;
  masterName: string;
  masterClass: string;
  membersCount: number;
  totalResets: number;
  score: number;
  castleStatus: string;
  emblemEmoji: string;
}

interface RankingEventPlayer {
  position: number;
  characterName: string;
  className: string;
  guildName: string;
  score: number | string;
  extraStat: string;
  subStat?: string;
  badge?: string;
}

interface ServerScheduleEvent {
  id: string;
  name: string;
  icon: string;
  category: 'events' | 'invasions';
  location: string;
  frequency: string;
  startTimeStr: string;
  startIntervalMin: number;
  startOffsetMin?: number;
  durationMin: number;
  colorTheme: string;
  rewardTag: string;
  description?: string;
  enabled?: boolean;
}

export function parseTimeToMinutes(timeStr: string): number {
  if (!timeStr || !timeStr.includes(':')) return 0;
  const parts = timeStr.split(':');
  const h = parseInt(parts[0], 10) || 0;
  const m = parseInt(parts[1], 10) || 0;
  return ((h % 24) * 60 + (m % 60));
}

export function formatMinutesToTime(totalMinutes: number): string {
  const normalized = ((Math.floor(totalMinutes) % 1440) + 1440) % 1440;
  const h = Math.floor(normalized / 60);
  const m = normalized % 60;
  return `${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}`;
}

export function computeDailySchedule(startTimeStr: string, intervalMin: number): string[] {
  const interval = Number(intervalMin);
  const initialTime = startTimeStr || '00:00';
  if (!interval || interval <= 0 || interval >= 1440) {
    return [initialTime];
  }

  const startMin = parseTimeToMinutes(initialTime);
  const timesSet = new Set<string>();
  let current = startMin;
  const maxOccurrences = Math.min(144, Math.floor(1440 / Math.max(10, interval)) + 2);

  for (let i = 0; i < maxOccurrences; i++) {
    const formatted = formatMinutesToTime(current);
    if (timesSet.has(formatted)) {
      break;
    }
    timesSet.add(formatted);
    current = (current + interval) % 1440;
  }

  const result = Array.from(timesSet);
  result.sort((a, b) => parseTimeToMinutes(a) - parseTimeToMinutes(b));
  return result.length > 0 ? result : [initialTime];
}

export function computeEventStatus(
  event: ServerScheduleEvent,
  now: Date
): {
  isOpen: boolean;
  isImminent: boolean;
  statusLabel: string;
  countdownFormatted: string;
  progressPct: number;
  scheduledTimes: string[];
  nextOccurrenceTime: string;
} {
  const startTime = event.startTimeStr || (event.startOffsetMin !== undefined ? formatMinutesToTime(event.startOffsetMin) : '00:00');
  const interval = Number(event.startIntervalMin) || 120;
  const duration = Number(event.durationMin) || 15;
  const scheduledTimes = computeDailySchedule(startTime, interval);

  const currentMinuteOfDay = now.getHours() * 60 + now.getMinutes() + now.getSeconds() / 60;
  const occurrenceMinutes = scheduledTimes.map(t => parseTimeToMinutes(t)).sort((a, b) => a - b);

  let isOpen = false;
  let remainingOpenSec = 0;
  let nextOccurrenceMin = -1;

  for (const t of occurrenceMinutes) {
    const windowStart = t;
    const windowEnd = t + duration;

    if (windowEnd <= 1440) {
      if (currentMinuteOfDay >= windowStart && currentMinuteOfDay < windowEnd) {
        isOpen = true;
        remainingOpenSec = Math.max(0, Math.floor((windowEnd - currentMinuteOfDay) * 60));
        break;
      }
    } else {
      const overflowEnd = windowEnd - 1440;
      if (currentMinuteOfDay >= windowStart || currentMinuteOfDay < overflowEnd) {
        isOpen = true;
        const diff = currentMinuteOfDay >= windowStart
          ? (windowEnd - currentMinuteOfDay)
          : (overflowEnd - currentMinuteOfDay);
        remainingOpenSec = Math.max(0, Math.floor(diff * 60));
        break;
      }
    }
  }

  if (isOpen) {
    const m = Math.floor(remainingOpenSec / 60);
    const s = remainingOpenSec % 60;
    const totalDurationSec = duration * 60;
    const progressPct = totalDurationSec > 0
      ? Math.min(100, Math.max(0, 100 - Math.floor((remainingOpenSec / totalDurationSec) * 100)))
      : 100;

    return {
      isOpen: true,
      isImminent: false,
      statusLabel: 'ABERTO AGORA',
      countdownFormatted: `Fecha em ${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`,
      progressPct,
      scheduledTimes,
      nextOccurrenceTime: 'Portões Abertos'
    };
  }

  for (const t of occurrenceMinutes) {
    if (t > currentMinuteOfDay) {
      nextOccurrenceMin = t;
      break;
    }
  }

  let diffMinutes = 0;
  if (nextOccurrenceMin !== -1) {
    diffMinutes = nextOccurrenceMin - currentMinuteOfDay;
  } else {
    const firstOccurTomorrow = occurrenceMinutes[0] !== undefined ? occurrenceMinutes[0] : parseTimeToMinutes(startTime);
    diffMinutes = (1440 - currentMinuteOfDay) + firstOccurTomorrow;
    nextOccurrenceMin = firstOccurTomorrow;
  }

  const remainingSec = Math.max(0, Math.floor(diffMinutes * 60));
  const h = Math.floor(remainingSec / 3600);
  const m = Math.floor((remainingSec % 3600) / 60);
  const s = remainingSec % 60;

  let countdownFormatted = '';
  if (h > 0) {
    countdownFormatted = `${String(h).padStart(2, '0')}h ${String(m).padStart(2, '0')}m ${String(s).padStart(2, '0')}s`;
  } else {
    countdownFormatted = `${String(m).padStart(2, '0')}m ${String(s).padStart(2, '0')}s`;
  }

  const isImminent = remainingSec <= 300;
  const statusLabel = isImminent
    ? 'INICIANDO!'
    : `Em ${h > 0 ? h + 'h ' : ''}${m}m`;

  const cycleMinutes = Math.min(1440, Math.max(duration, interval));
  const progressPct = Math.min(100, Math.max(0, Math.floor(((cycleMinutes - diffMinutes) / cycleMinutes) * 100)));

  return {
    isOpen: false,
    isImminent,
    statusLabel,
    countdownFormatted,
    progressPct,
    scheduledTimes,
    nextOccurrenceTime: formatMinutesToTime(nextOccurrenceMin)
  };
}

const DEFAULT_SERVER_EVENTS: ServerScheduleEvent[] = [
  {
    id: 'blood-castle',
    name: 'Blood Castle',
    icon: '🩸',
    category: 'events',
    location: 'Igreja de Devias (NPC Archangel)',
    frequency: 'A cada 2 horas (00:00, 02:00, 04:00...)',
    startTimeStr: '00:00',
    startIntervalMin: 120,
    startOffsetMin: 0,
    durationMin: 15,
    colorTheme: '#f43f5e',
    rewardTag: 'Archangel Weapon • Chaos & Creation',
    description: 'Atravesse a ponte de Devias, destrua o portão da igreja e entregue a arma sagrada ao Arcanjo ferido.',
    enabled: true
  },
  {
    id: 'devil-square',
    name: 'Devil Square',
    icon: '😈',
    category: 'events',
    location: 'Noria (NPC Charon)',
    frequency: 'A cada 2 horas (00:30, 02:30, 04:30...)',
    startTimeStr: '00:30',
    startIntervalMin: 120,
    startOffsetMin: 30,
    durationMin: 20,
    colorTheme: '#a855f7',
    rewardTag: 'Hordas de Mobs • Joias & XP 200%',
    description: 'Sobreviva a 4 ondas contínuas de monstros demoníacos com taxa de experiência e drop aumentados.',
    enabled: true
  },
  {
    id: 'golden-invasion',
    name: 'Invasão Dourada (Dragões)',
    icon: '🐲',
    category: 'invasions',
    location: 'Lorencia, Noria, Devias, Tarkan',
    frequency: 'A cada 3 horas (00:15, 03:15, 06:15...)',
    startTimeStr: '00:15',
    startIntervalMin: 180,
    startOffsetMin: 15,
    durationMin: 25,
    colorTheme: '#eab308',
    rewardTag: 'Box of Kundun +1 a +5 (Itens Exc)',
    description: 'Dragões dourados e goblins reluzentes invadem os continentes carregando caixas de Kundun.',
    enabled: true
  },
  {
    id: 'chaos-castle',
    name: 'Chaos Castle',
    icon: '⚔️',
    category: 'events',
    location: 'Armor of Guardsman (Todos os Mapas)',
    frequency: 'A cada 2 horas (01:00, 03:00, 05:00...)',
    startTimeStr: '01:00',
    startIntervalMin: 120,
    startOffsetMin: 60,
    durationMin: 10,
    colorTheme: '#f97316',
    rewardTag: 'Itens Ancient • Joia Life & Harmony',
    description: 'Arena battle royale individual com armaduras idênticas e piso dinâmico desmoronando.',
    enabled: true
  },
  {
    id: 'illusion-temple',
    name: 'Illusion Temple',
    icon: '🔮',
    category: 'events',
    location: 'Elbeland (NPC Mirage)',
    frequency: 'A cada 4 horas (00:45, 04:45, 08:45...)',
    startTimeStr: '00:45',
    startIntervalMin: 240,
    startOffsetMin: 45,
    durationMin: 20,
    colorTheme: '#06b6d4',
    rewardTag: 'Relíquias Sagradas • Fenrir Mats',
    description: 'Guerra tática em equipes 5x5 pela posse e consagração da Esfera de Ilusão no altar.',
    enabled: true
  },
  {
    id: 'white-wizard',
    name: 'Mago Branco & Orcs',
    icon: '🧙‍♂️',
    category: 'invasions',
    location: 'Lorencia, Noria e Devias',
    frequency: 'A cada 2 horas (00:40, 02:40, 04:40...)',
    startTimeStr: '00:40',
    startIntervalMin: 120,
    startOffsetMin: 40,
    durationMin: 15,
    colorTheme: '#38bdf8',
    rewardTag: 'Wizard Ring (+10% Dano) • Joias Bless',
    description: 'O terrível White Wizard e seu exército de Destler e Orcs marcham contra as capitais.',
    enabled: true
  },
  {
    id: 'battle-soccer',
    name: 'Battle Soccer',
    icon: '⚽',
    category: 'events',
    location: 'Lorencia Stadium (Arena)',
    frequency: 'A cada 1 hora (00:10, 01:10, 02:10...)',
    startTimeStr: '00:10',
    startIntervalMin: 60,
    startOffsetMin: 10,
    durationMin: 15,
    colorTheme: '#10b981',
    rewardTag: 'Guerra de Guilds • Pontos de Honra',
    description: 'Duelo esportivo e combate de sobrevivência entre Guilds no estádio de Lorencia.',
    enabled: true
  },
  {
    id: 'castle-siege',
    name: 'Castle Siege',
    icon: '🏰',
    category: 'events',
    location: 'Valley of Loren',
    frequency: 'Todo Domingo às 20h00',
    startTimeStr: '20:00',
    startIntervalMin: 1440,
    startOffsetMin: 1200,
    durationMin: 120,
    colorTheme: '#d4af37',
    rewardTag: 'Trono do Castelo • Land of Trials',
    description: 'A guerra suprema de clãs pelo trono do Castelo, acesso exclusivo a Land of Trials e taxas.',
    enabled: true
  }
];

interface LiveEventDisplay {
  id: string;
  name: string;
  icon: string;
  category: 'events' | 'invasions';
  location: string;
  frequency: string;
  startTimeStr: string;
  startIntervalMin: number;
  durationMin: number;
  scheduledTimes: string[];
  nextOccurrenceTime: string;
  colorTheme: string;
  rewardTag: string;
  isOpen: boolean;
  isImminent: boolean;
  statusLabel: string;
  countdownFormatted: string;
  progressPct: number;
  enabled?: boolean;
}

interface ClassDetail {
  id: string;
  name: string;
  role: string;
  icon: string;
  weapon: string;
  description: string;
  keySkills: string[];
  stats: { str: number; agi: number; vit: number; ene: number; com?: number };
  quote: string;
}

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-root',
  imports: [ReactiveFormsModule, MatIconModule, DecimalPipe],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App implements OnInit, OnDestroy {
  private readonly platformId = inject(PLATFORM_ID);
  readonly isBrowser = isPlatformBrowser(this.platformId);
  private timerInterval: ReturnType<typeof setInterval> | null = null;
  readonly currentTime = signal<Date>(new Date());

  // Active View State
  readonly currentTab = signal<'home' | 'register' | 'classes' | 'ranking' | 'download' | 'account' | 'events'>('home');
  readonly rankingTab = signal<'level' | 'resets' | 'deaths' | 'guilds' | 'bloodcastle' | 'devilsquare' | 'chaoscastle' | 'illusiontemple' | 'battlesoccer'>('guilds');
  readonly accountSubTab = signal<'characters' | 'distribute' | 'security' | 'vip' | 'gm_events'>('characters');
  readonly heroEventCategory = signal<'all' | 'events' | 'invasions'>('all');
  readonly isLoginOpen = signal<boolean>(false);
  readonly isMuted = signal<boolean>(true);
  readonly showPassword = signal<boolean>(false);
  readonly showSuccessToast = signal<string | null>(null);

  // GM / Grand Master Profile Check
  readonly isGM = computed<boolean>(() => {
    const user = this.currentUser();
    if (!user) return false;
    return user.isAdmin === true || user.role === 'GM';
  });

  // Server event schedules. Single source of truth is the backend
  // (/api/events/schedule for public reads, /api/events/config for the GM panel).
  readonly serverEventsList = signal<ServerScheduleEvent[]>([]);

  // Events Dashboard Management State
  readonly eventSearchQuery = signal<string>('');
  readonly eventFilterCategory = signal<'all' | 'events' | 'invasions' | 'active'>('all');
  readonly isEventModalOpen = signal<boolean>(false);
  readonly editingEventId = signal<string | null>(null);

  // Presets for the configuration modal
  readonly iconPresets = ['🩸', '😈', '🐲', '⚔️', '🔮', '🧙‍♂️', '⚽', '🏰', '👑', '🐍', '💎', '⚡', '🛡️', '💀', '👹', '🏹', '🏆', '🔥', '🌟', '💰'];
  readonly colorPresets = [
    { label: 'Rubi', color: '#f43f5e' },
    { label: 'Roxo', color: '#a855f7' },
    { label: 'Dourado', color: '#eab308' },
    { label: 'Laranja', color: '#f97316' },
    { label: 'Ciano', color: '#06b6d4' },
    { label: 'Azul', color: '#38bdf8' },
    { label: 'Verde', color: '#10b981' },
    { label: 'Ouro MU', color: '#d4af37' },
    { label: 'Rosa', color: '#ec4899' },
  ];
  readonly startTimePresets = [
    '00:00', '00:15', '00:30', '00:40', '00:45',
    '01:00', '01:30', '02:00', '04:00', '20:00'
  ];
  readonly intervalPresets = [
    { label: '30m', value: 30 },
    { label: '1h (60m)', value: 60 },
    { label: '2h (120m)', value: 120 },
    { label: '3h (180m)', value: 180 },
    { label: '4h (240m)', value: 240 },
    { label: '6h (360m)', value: 360 },
    { label: '12h (720m)', value: 720 },
    { label: '24h / Diário', value: 1440 }
  ];
  readonly durationPresets = [
    { label: '5m', value: 5 },
    { label: '10m', value: 10 },
    { label: '15m', value: 15 },
    { label: '20m', value: 20 },
    { label: '25m', value: 25 },
    { label: '30m', value: 30 },
    { label: '60m', value: 60 },
    { label: '120m', value: 120 }
  ];

  // Dynamic reactive state for modal schedule preview
  readonly modalCurrentStartTime = signal<string>('00:00');
  readonly modalCurrentInterval = signal<number>(240);
  readonly modalScheduledTimes = computed(() => {
    return computeDailySchedule(this.modalCurrentStartTime(), this.modalCurrentInterval());
  });

  readonly eventForm = new FormGroup({
    id: new FormControl<string>(''),
    name: new FormControl<string>('', { nonNullable: true, validators: [Validators.required, Validators.minLength(2)] }),
    category: new FormControl<'events' | 'invasions'>('events', { nonNullable: true, validators: [Validators.required] }),
    icon: new FormControl<string>('⚔️', { nonNullable: true, validators: [Validators.required] }),
    colorTheme: new FormControl<string>('#d4af37', { nonNullable: true, validators: [Validators.required] }),
    location: new FormControl<string>('', { nonNullable: true, validators: [Validators.required] }),
    frequency: new FormControl<string>('', { nonNullable: true }),
    startTimeStr: new FormControl<string>('00:00', { nonNullable: true, validators: [Validators.required] }),
    startIntervalMin: new FormControl<number>(240, { nonNullable: true, validators: [Validators.required, Validators.min(10), Validators.max(1440)] }),
    startOffsetMin: new FormControl<number>(0, { nonNullable: true }),
    durationMin: new FormControl<number>(15, { nonNullable: true, validators: [Validators.required, Validators.min(1), Validators.max(720)] }),
    rewardTag: new FormControl<string>('', { nonNullable: true, validators: [Validators.required] }),
    description: new FormControl<string>(''),
    enabled: new FormControl<boolean>(true, { nonNullable: true })
  });

  // Filtered and enriched event items for the Events Dashboard
  readonly filteredEventsPageList = computed(() => {
    const list = this.serverEventsList();
    const cat = this.eventFilterCategory();
    const query = this.eventSearchQuery().toLowerCase().trim();
    const now = this.currentTime();

    return list
      .filter(e => {
        if (cat === 'events' && e.category !== 'events') return false;
        if (cat === 'invasions' && e.category !== 'invasions') return false;
        if (cat === 'active' && e.enabled === false) return false;
        if (query) {
          const matchName = e.name.toLowerCase().includes(query);
          const matchLoc = e.location.toLowerCase().includes(query);
          const matchRew = e.rewardTag.toLowerCase().includes(query);
          if (!matchName && !matchLoc && !matchRew) return false;
        }
        return true;
      })
      .map(event => {
        const status = computeEventStatus(event, now);
        const startTimeStr = event.startTimeStr || (event.startOffsetMin !== undefined ? formatMinutesToTime(event.startOffsetMin) : '00:00');

        return {
          ...event,
          startTimeStr,
          isOpen: status.isOpen,
          isImminent: status.isImminent,
          statusLabel: status.statusLabel,
          countdownFormatted: status.countdownFormatted,
          progressPct: status.progressPct,
          scheduledTimes: status.scheduledTimes,
          nextOccurrenceTime: status.nextOccurrenceTime
        };
      });
  });

  // Overall event metrics for the dashboard header
  readonly eventsStats = computed(() => {
    const list = this.serverEventsList();
    const live = this.filteredEventsPageList();
    const total = list.length;
    const active = list.filter(e => e.enabled !== false).length;
    const openNow = live.filter(e => e.isOpen && e.enabled !== false).length;
    const imminent = live.filter(e => e.isImminent && e.enabled !== false).length;
    return { total, active, openNow, imminent };
  });

  // Live events displayed on Index Hero Card (only active events)
  readonly liveEvents = computed<LiveEventDisplay[]>(() => {
    const now = this.currentTime();
    const cat = this.heroEventCategory();
    const enabledList = this.serverEventsList().filter(e => e.enabled !== false);
    const filtered = cat === 'all'
      ? enabledList
      : enabledList.filter(e => e.category === cat);

    return filtered.map(event => {
      const status = computeEventStatus(event, now);
      const startTimeStr = event.startTimeStr || (event.startOffsetMin !== undefined ? formatMinutesToTime(event.startOffsetMin) : '00:00');

      return {
        id: event.id,
        name: event.name,
        icon: event.icon,
        category: event.category,
        location: event.location,
        frequency: event.frequency,
        startTimeStr,
        startIntervalMin: event.startIntervalMin,
        durationMin: event.durationMin,
        scheduledTimes: status.scheduledTimes,
        nextOccurrenceTime: status.nextOccurrenceTime,
        colorTheme: event.colorTheme,
        rewardTag: event.rewardTag,
        isOpen: status.isOpen,
        isImminent: status.isImminent,
        statusLabel: status.statusLabel,
        countdownFormatted: status.countdownFormatted,
        progressPct: status.progressPct,
        enabled: event.enabled
      };
    });
  });

  // Server stats & timings
  readonly serverInfo = signal({
    serverName: 'MU FREE',
    season: 'Season 6',
    version: 'Episódio 3 (S6E3)',
    onlinePlayers: 0,
    peakOnline: 0,
    totalAccounts: 0,
    expRate: '150x',
    dropRate: '40%',
    status: 'ONLINE',
    castleSiege: 'Domingo às 20:00 (LorenDeep)',
    bloodCastle: 'A cada 2 horas',
    chaosCastle: 'A cada 4 horas',
    devilSquare: 'A cada 2 horas',
    serverIp: 'connect.mufree.com:44405'
  });

  readonly lastUpdate = signal<string>('Agora mesmo');

  // Selected Class Modal / Detail
  readonly selectedClass = signal<ClassDetail | null>(null);

  // User Authentication State
  readonly currentUser = signal<AccountData | null>(null);

  // Login form
  readonly loginForm = new FormGroup({
    loginUser: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    loginPassword: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });

  readonly loginError = signal<string | null>(null);
  readonly isLoggingIn = signal<boolean>(false);

  // Register form
  readonly registerForm = new FormGroup({
    loginName: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.pattern(/^[a-zA-Z0-9]{3,10}$/)],
    }),
    password: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.minLength(6)],
    }),
    confirmPassword: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required],
    }),
    email: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.email],
    }),
    securityCode: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.minLength(4), Validators.maxLength(10)],
    }),
    terms: new FormControl(false, {
      nonNullable: true,
      validators: [Validators.requiredTrue],
    }),
  });

  readonly isRegistering = signal<boolean>(false);
  readonly registerSuccess = signal<string | null>(null);
  readonly registerError = signal<string | null>(null);

  // Password strength
  readonly passwordStrength = computed(() => {
    const pwd = this.registerForm.controls.password.value;
    if (!pwd) return { pct: 0, text: 'Muito curta', color: '#64748b' };
    if (pwd.length < 6) return { pct: 25, text: 'Fraca (mínimo 6 caracteres)', color: '#ef4444' };
    let score = 0;
    if (pwd.length >= 8) score++;
    if (/[A-Z]/.test(pwd)) score++;
    if (/[0-9]/.test(pwd)) score++;
    if (/[^a-zA-Z0-9]/.test(pwd)) score++;

    if (score <= 1) return { pct: 50, text: 'Média', color: '#f59e0b' };
    if (score === 2) return { pct: 75, text: 'Boa', color: '#38bdf8' };
    return { pct: 100, text: 'Forte e Segura', color: '#10b981' };
  });

  // Ranking data
  readonly heroRankTab = signal<'resets' | 'kills' | 'pvp'>('resets');

  readonly guildRanking = signal<RankingGuild[]>([]);

  readonly resetsRanking = signal<RankingPlayer[]>([]);

  readonly bloodCastleRanking = signal<RankingEventPlayer[]>([]);

  readonly devilSquareRanking = signal<RankingEventPlayer[]>([]);

  readonly chaosCastleRanking = signal<RankingEventPlayer[]>([]);

  readonly illusionTempleRanking = signal<RankingEventPlayer[]>([]);

  readonly battleSoccerRanking = signal<RankingEventPlayer[]>([]);

  readonly levelRanking = signal<RankingPlayer[]>([]);

  readonly deathsRanking = signal<RankingPlayer[]>([]);

  readonly pvpRanking = signal<RankingPlayer[]>([]);

  readonly rankingSearch = signal<string>('');

  readonly filteredGuildRanking = computed(() => {
    const q = this.rankingSearch().toLowerCase().trim();
    if (!q) return this.guildRanking();
    return this.guildRanking().filter(
      g => g.guildName.toLowerCase().includes(q) || g.masterName.toLowerCase().includes(q)
    );
  });

  readonly filteredResetsRanking = computed(() => {
    const q = this.rankingSearch().toLowerCase().trim();
    if (!q) return this.resetsRanking();
    return this.resetsRanking().filter(
      p => p.characterName.toLowerCase().includes(q) || p.guildName.toLowerCase().includes(q)
    );
  });

  readonly filteredBloodCastleRanking = computed(() => {
    const q = this.rankingSearch().toLowerCase().trim();
    if (!q) return this.bloodCastleRanking();
    return this.bloodCastleRanking().filter(
      p => p.characterName.toLowerCase().includes(q) || p.guildName.toLowerCase().includes(q)
    );
  });

  readonly filteredDevilSquareRanking = computed(() => {
    const q = this.rankingSearch().toLowerCase().trim();
    if (!q) return this.devilSquareRanking();
    return this.devilSquareRanking().filter(
      p => p.characterName.toLowerCase().includes(q) || p.guildName.toLowerCase().includes(q)
    );
  });

  readonly filteredChaosCastleRanking = computed(() => {
    const q = this.rankingSearch().toLowerCase().trim();
    if (!q) return this.chaosCastleRanking();
    return this.chaosCastleRanking().filter(
      p => p.characterName.toLowerCase().includes(q) || p.guildName.toLowerCase().includes(q)
    );
  });

  readonly filteredIllusionTempleRanking = computed(() => {
    const q = this.rankingSearch().toLowerCase().trim();
    if (!q) return this.illusionTempleRanking();
    return this.illusionTempleRanking().filter(
      p => p.characterName.toLowerCase().includes(q) || p.guildName.toLowerCase().includes(q)
    );
  });

  readonly filteredBattleSoccerRanking = computed(() => {
    const q = this.rankingSearch().toLowerCase().trim();
    if (!q) return this.battleSoccerRanking();
    return this.battleSoccerRanking().filter(
      p => p.characterName.toLowerCase().includes(q) || p.guildName.toLowerCase().includes(q)
    );
  });

  readonly filteredLevelRanking = computed(() => {
    const q = this.rankingSearch().toLowerCase().trim();
    if (!q) return this.levelRanking().slice(0, 5);
    return this.levelRanking().filter(
      p => p.characterName.toLowerCase().includes(q) || p.guildName.toLowerCase().includes(q)
    );
  });

  readonly filteredDeathsRanking = computed(() => {
    const q = this.rankingSearch().toLowerCase().trim();
    if (!q) return this.deathsRanking().slice(0, 5);
    return this.deathsRanking().filter(
      p => p.characterName.toLowerCase().includes(q) || p.guildName.toLowerCase().includes(q)
    );
  });

  // Class encyclopedia
  readonly classesList: ClassDetail[] = [
    {
      id: 'dark-knight',
      name: 'Blade Knight',
      role: 'Combatente Físico / Tanque',
      icon: '⚔️',
      weapon: 'Espadas Duplas, Machados e Lanças',
      description: 'Guerreiro destemido com alta força e vitalidade. Especialista em combos corporais avassaladores e mestre em PVP corpo-a-corpo.',
      keySkills: ['Twisting Slash', 'Death Stab', 'Greater Fortitude', 'Rageful Blow', 'Combo Atk'],
      stats: { str: 28, agi: 20, vit: 25, ene: 10 },
      quote: 'A honra de Lorencia forjada em aço e sangue de dragão.'
    },
    {
      id: 'fairy-elf',
      name: 'Fairy / Muse Elf',
      role: 'Arqueira / Suporte Sagrado',
      icon: '🏹',
      weapon: 'Arcos, Bestas e Flechas Infinitas',
      description: 'Belíssima elfa com agilidade formidável. Domina tiros perfurantes à distância e magias sagradas de cura, buff de ataque e defesa.',
      keySkills: ['Penetration', 'Ice Arrow', 'Multi-Shot', 'Heal & Greater Defense', 'Summon Bali'],
      stats: { str: 22, agi: 25, vit: 20, ene: 15 },
      quote: 'Os ventos de Noria guiam minhas flechas com precisão letal.'
    },
    {
      id: 'dark-wizard',
      name: 'Soul Master',
      role: 'Mago Supremo / Dano em Área',
      icon: '🔮',
      weapon: 'Cajados Arcanos e Escudos Mágicos',
      description: 'Mestre das forças arcanas e elementais. Capaz de invocar tempestades de espíritos, meteoros e criar barreiras mágicas intransponíveis.',
      keySkills: ['Evil Spirits', 'Aqua Beam', 'Soul Barrier', 'Ice Storm', 'Teleport'],
      stats: { str: 18, agi: 18, vit: 15, ene: 30 },
      quote: 'O conhecimento dos antigos dobrará qualquer inimigo.'
    },
    {
      id: 'magic-gladiator',
      name: 'Magic Gladiator',
      role: 'Híbrido Mágico & Espadachim',
      icon: '⚡',
      weapon: 'Espadas Mágicas Rúnicas e Cajados',
      description: 'Combina maestria com espadas e feitiços devastadores. Ganha 7 pontos de status por level e pode equipar magias de guerreiro e mago.',
      keySkills: ['Fire Slash', 'Power Slash', 'Gigantic Storm', 'Flame', 'Twisting Slash'],
      stats: { str: 26, agi: 26, vit: 26, ene: 26 },
      quote: 'O equilíbrio perfeito entre a força bruta e a energia cósmica.'
    },
    {
      id: 'dark-lord',
      name: 'Dark Lord',
      role: 'Líder Soberano / Convocador',
      icon: '👑',
      weapon: 'Cetros Sagrados, Dark Horse & Dark Raven',
      description: 'Comanda montarias lendárias, invocações cósmicas e lidera guildas com maestria através do atributo Comando (Command).',
      keySkills: ['Fire Scream', 'Earthquake', 'Electric Spark', 'Force Wave', 'Critical Damage'],
      stats: { str: 26, agi: 20, vit: 20, ene: 15, com: 25 },
      quote: 'A soberania de Valley of Loren pertence aos destemidos.'
    },
    {
      id: 'summoner',
      name: 'Summoner / Dimension Master',
      role: 'Manipuladora Dimensional / Debuff',
      icon: '✨',
      weapon: 'Livros de Magia e Bastões Místicos',
      description: 'Dom natural em magia dimensional e maldições. Drena a vida de adversários, reduz defesas e invoca criaturas das trevas.',
      keySkills: ['Lightning Shock', 'Chain Lightning', 'Sleep & Weakness', 'Damage Reflection', 'Sahamutt'],
      stats: { str: 21, agi: 21, vit: 18, ene: 23 },
      quote: 'As portas de Elbeland se abrem para os segredos proibidos.'
    },
    {
      id: 'rage-fighter',
      name: 'Rage Fighter / Fist Master',
      role: 'Lutador Brutal / Dano Físico Pesado',
      icon: '🥊',
      weapon: 'Garras Sagradas (Claws) e Punhos',
      description: 'Guerreiro de Karutan descendente dos cavaleiros reais. Desfere socos sônicos implacáveis com base na sua Vitalidade e Energia.',
      keySkills: ['Dragon Roar', 'Dark Side', 'Large Ring Blower', 'Chain Drive', 'Phoenix Shot'],
      stats: { str: 32, agi: 27, vit: 25, ene: 20 },
      quote: 'A fúria de Karutan despedaça qualquer armadura com a força dos punhos.'
    }
  ];

  ngOnInit(): void {
    if (this.isBrowser) {
      this.updateClock();
      // Start real-time 1s ticker for live event countdowns & server clock
      this.timerInterval = setInterval(() => {
        this.currentTime.set(new Date());
        this.updateClock();
      }, 1000);

      // Load the real event schedule from the backend (single source of truth).
      this.loadEventsFromApi();

      // Load real server info (online players, total accounts) from the backend.
      this.loadServerInfo();

      // Load real rankings from the backend.
      this.loadRankings();

      // Restore session from backend
      this.loadSession();
    }
  }

  ngOnDestroy(): void {
    if (this.timerInterval) {
      clearInterval(this.timerInterval);
      this.timerInterval = null;
    }
  }

  private async loadEventsFromApi(): Promise<void> {
    if (!this.isBrowser) return;
    try {
      const response = await fetch('/api/events/schedule');
      if (response.ok) {
        const data = await response.json();
        if (data.success && Array.isArray(data.events)) {
          const normalized = (data.events as ServerScheduleEvent[]).map((e: ServerScheduleEvent) => ({
            ...e,
            startTimeStr: e.startTimeStr || (e.startOffsetMin !== undefined ? formatMinutesToTime(e.startOffsetMin) : '00:00'),
            startIntervalMin: Number(e.startIntervalMin) || 120,
            durationMin: Number(e.durationMin) || 15
          }));
          this.serverEventsList.set(normalized);
        }
      }
    } catch (err) {
      console.warn('Erro ao carregar eventos do servidor:', err);
    }
  }

  private async loadSession(): Promise<void> {
    if (!this.isBrowser) return;
    try {
      const sessionResponse = await fetch('/api/session', { credentials: 'same-origin' });
      if (sessionResponse.ok) {
        const sessionData = await sessionResponse.json();
        if (sessionData.success && sessionData.loggedIn) {
          const meResponse = await fetch('/api/me', { credentials: 'same-origin' });
          if (meResponse.ok) {
            const meData = await meResponse.json();
            if (meData.success && meData.account) {
              const account = meData.account;
              const characters: Character[] = (account.characters || []).map((c: any) => ({
                id: c.id,
                name: c.name,
                className: c.className,
                classCode: c.classCode,
                guild: c.guild,
                level: c.level,
                masterLevel: c.masterLevel,
                kills: c.kills,
                resets: c.resets,
                masterResets: c.masterResets,
                strength: c.strength,
                agility: c.agility,
                vitality: c.vitality,
                energy: c.energy,
                status: c.status
              }));
              this.currentUser.set({
                loginName: account.loginName,
                email: account.email,
                role: account.role,
                isAdmin: account.isAdmin,
                characters
              });
              return;
            }
          }
        }
      }
      this.currentUser.set(null);
    } catch (err) {
      console.warn('Erro ao restaurar sessão:', err);
      this.currentUser.set(null);
    }
  }

  private async loadServerInfo(): Promise<void> {
    if (!this.isBrowser) return;
    try {
      const response = await fetch('/api/server-info', { credentials: 'same-origin' });
      if (response.ok) {
        const data = await response.json();
        this.serverInfo.update(info => ({
          ...info,
          onlinePlayers: Number(data.onlinePlayers) || 0,
          totalAccounts: Number(data.totalAccounts) || 0,
          status: (data.status || 'online').toUpperCase()
        }));
      }
    } catch (err) {
      console.warn('Erro ao carregar informações do servidor:', err);
    }
  }

  private async loadRankings(): Promise<void> {
    if (!this.isBrowser) return;
    try {
      const [levelRes, deathsRes, resetsRes, guildsRes, bcRes, dsRes, ccRes] = await Promise.all([
        fetch('/api/ranking', { credentials: 'same-origin' }),
        fetch('/api/ranking/deaths', { credentials: 'same-origin' }),
        fetch('/api/ranking/resets', { credentials: 'same-origin' }),
        fetch('/api/ranking/guilds', { credentials: 'same-origin' }),
        fetch('/api/ranking/events/blood-castle', { credentials: 'same-origin' }),
        fetch('/api/ranking/events/devil-square', { credentials: 'same-origin' }),
        fetch('/api/ranking/events/chaos-castle', { credentials: 'same-origin' }),
      ]);

      const parse = async (res: Response) => {
        if (!res.ok) return [];
        const data = await res.json();
        return data.success ? (data.ranking || []) : [];
      };

      const [level, deaths, resets, guilds, bc, ds, cc] = await Promise.all([
        parse(levelRes), parse(deathsRes), parse(resetsRes), parse(guildsRes), parse(bcRes), parse(dsRes), parse(ccRes)
      ]);

      this.levelRanking.set(level);
      this.deathsRanking.set(deaths);
      this.resetsRanking.set(resets);
      this.guildRanking.set((guilds as any[]).map((g: any) => ({
        ...g,
        emblemEmoji: g.emblemEmoji || '🛡️',
        castleStatus: g.castleStatus || 'Guild Registrada'
      })));
      this.bloodCastleRanking.set(bc);
      this.devilSquareRanking.set(ds);
      this.chaosCastleRanking.set(cc);
    } catch (err) {
      console.warn('Erro ao carregar rankings:', err);
    }
  }

  updateClock(): void {
    const now = new Date();
    const hours = String(now.getHours()).padStart(2, '0');
    const mins = String(now.getMinutes()).padStart(2, '0');
    this.lastUpdate.set(`${hours}:${mins} (Horário do Servidor)`);
  }

  navigateTo(tab: 'home' | 'register' | 'classes' | 'ranking' | 'download' | 'account' | 'events', subTab?: 'characters' | 'distribute' | 'security' | 'vip' | 'gm_events'): void {
    if (tab === 'events' && !this.isGM()) {
      this.showToast('Acesso Restrito: O painel de gestão de eventos é exclusivo para Grand Masters (GM).');
      this.currentTab.set('home');
      return;
    }
    if (subTab) {
      this.accountSubTab.set(subTab);
    }
    this.currentTab.set(tab);
    if (this.isBrowser) {
      window.scrollTo({ top: 0, behavior: 'smooth' });
    }
  }

  setRankingTab(tab: 'level' | 'resets' | 'deaths' | 'guilds' | 'bloodcastle' | 'devilsquare' | 'chaoscastle' | 'illusiontemple' | 'battlesoccer'): void {
    this.rankingTab.set(tab);
  }

  openLogin(): void {
    this.loginError.set(null);
    this.isLoginOpen.set(true);
  }

  closeLogin(): void {
    this.isLoginOpen.set(false);
  }

  togglePasswordVisibility(): void {
    this.showPassword.update(v => !v);
  }

  onLoginSubmit(): void {
    if (this.loginForm.invalid) {
      this.loginError.set('Preencha login e senha corretamente');
      return;
    }

    const { loginUser, loginPassword } = this.loginForm.getRawValue();
    this.isLoggingIn.set(true);
    this.loginError.set(null);

    fetch('/api/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'same-origin',
      body: JSON.stringify({ loginName: loginUser, password: loginPassword })
    })
      .then(response => response.json())
      .then(data => {
        this.isLoggingIn.set(false);
        if (data.success) {
          this.isLoginOpen.set(false);
          this.showToast(`Login realizado com sucesso! Bem-vindo(a), ${data.loginName}.`);
          this.loadSession().then(() => {
            this.navigateTo('account');
          });
        } else {
          this.loginError.set(data.message || 'Login ou senha inválidos');
        }
      })
      .catch(err => {
        this.isLoggingIn.set(false);
        console.error('Erro no login:', err);
        this.loginError.set('Erro interno do servidor');
      });
  }

  onLogout(): void {
    fetch('/api/logout', {
      method: 'POST',
      credentials: 'same-origin'
    })
      .then(() => {
        this.currentUser.set(null);
        this.showToast('Sessão encerrada com sucesso.');
        this.navigateTo('home');
      })
      .catch(err => {
        console.error('Erro no logout:', err);
        this.currentUser.set(null);
        this.showToast('Sessão encerrada.');
        this.navigateTo('home');
      });
  }

  onRegisterSubmit(): void {
    if (this.registerForm.invalid) {
      this.registerError.set('Por favor, preencha todos os campos corretamente e aceite os termos.');
      return;
    }

    const { loginName, password, confirmPassword, email, securityCode } = this.registerForm.getRawValue();

    if (password !== confirmPassword) {
      this.registerError.set('As senhas não coincidem!');
      return;
    }

    this.isRegistering.set(true);
    this.registerError.set(null);
    this.registerSuccess.set(null);

    fetch('/api/register', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'same-origin',
      body: JSON.stringify({ loginName, password, email, securityCode })
    })
      .then(response => response.json())
      .then(data => {
        this.isRegistering.set(false);
        if (data.success) {
          this.registerSuccess.set(loginName);
          this.showToast('Conta criada com sucesso! Faça login para acessar o painel.');
          this.registerForm.reset();
        } else {
          this.registerError.set(data.message || 'Erro ao criar conta');
        }
      })
      .catch(err => {
        this.isRegistering.set(false);
        console.error('Erro no registro:', err);
        this.registerError.set('Erro interno do servidor');
      });
  }

  hideRegisterError(): void {
    this.registerError.set(null);
  }

  openClassModal(cls: ClassDetail): void {
    this.selectedClass.set(cls);
  }

  closeClassModal(): void {
    this.selectedClass.set(null);
  }

  copyServerIp(): void {
    if (this.isBrowser && navigator.clipboard) {
      navigator.clipboard.writeText(this.serverInfo().serverIp);
      this.showToast('IP do servidor copiado para a área de transferência!');
    }
  }

  performReset(char: Character): void {
    if (char.level < 350) {
      this.showToast(`O personagem precisa ser Level 350+ para resetar. Atual: Lv ${char.level}`);
      return;
    }
    this.showToast('Operação disponível apenas no cliente do jogo.');
  }

  // Change password form
  readonly changePasswordForm = new FormGroup({
    currentPassword: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    newPassword: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(6)] }),
    confirmNewPassword: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    securityCode: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.minLength(4), Validators.maxLength(10)]
    }),
  });

  performClearPk(char: Character): void {
    const currentAcc = this.currentUser();
    if (!currentAcc) return;

    if (char.kills === 0) {
      this.showToast(`${char.name} é um Herói / Neutro e não possui status de PK.`);
      return;
    }
    this.showToast('Operação disponível apenas no cliente do jogo.');
  }

  performUnbug(char: Character): void {
    this.showToast('Operação disponível apenas no cliente do jogo.');
  }

  performAddPoints(char: Character, stat: 'str' | 'agi' | 'vit' | 'ene', amount: number): void {
    const currentAcc = this.currentUser();
    if (!currentAcc) return;
    this.showToast('Operação disponível apenas no cliente do jogo.');
  }

  onChangePasswordSubmit(): void {
    if (this.changePasswordForm.invalid) {
      this.showToast('Por favor, preencha todos os campos, incluindo o Código de Segurança (Personal ID).');
      return;
    }

    const { currentPassword, newPassword, confirmNewPassword, securityCode } = this.changePasswordForm.getRawValue();
    if (newPassword !== confirmNewPassword) {
      this.showToast('A nova senha e a confirmação não coincidem.');
      return;
    }

    const currentAcc = this.currentUser();
    if (!currentAcc) {
      this.showToast('Sessão expirada. Faça login novamente.');
      return;
    }

    this.showToast('Operação disponível apenas no cliente do jogo.');
  }

  // ==========================================================
  // EVENT MANAGEMENT & DASHBOARD METHODS
  // ==========================================================

  setEventFilterCategory(cat: 'all' | 'events' | 'invasions' | 'active'): void {
    this.eventFilterCategory.set(cat);
  }

  openCreateEventModal(): void {
    this.editingEventId.set(null);
    this.modalCurrentStartTime.set('00:00');
    this.modalCurrentInterval.set(240);
    this.eventForm.reset({
      id: '',
      name: '',
      category: 'events',
      icon: '⚔️',
      colorTheme: '#d4af37',
      location: 'Lorencia (NPC Central)',
      frequency: 'A cada 4 horas (00:00, 04:00, 08:00, 12:00, 16:00, 20:00)',
      startTimeStr: '00:00',
      startIntervalMin: 240,
      startOffsetMin: 0,
      durationMin: 15,
      rewardTag: 'Joias, XP Bônus & Box of Kundun',
      description: 'Evento especial com cronograma configurado para horários específicos do dia.',
      enabled: true
    });
    this.updateFrequencyHint();
    this.isEventModalOpen.set(true);
  }

  openEditEventModal(event: ServerScheduleEvent, $event?: Event): void {
    if ($event) {
      $event.stopPropagation();
    }
    this.editingEventId.set(event.id);
    const startTime = event.startTimeStr || (event.startOffsetMin !== undefined ? formatMinutesToTime(event.startOffsetMin) : '00:00');
    const interval = Number(event.startIntervalMin) || 120;

    this.modalCurrentStartTime.set(startTime);
    this.modalCurrentInterval.set(interval);

    this.eventForm.patchValue({
      id: event.id,
      name: event.name,
      category: event.category,
      icon: event.icon,
      colorTheme: event.colorTheme,
      location: event.location,
      frequency: event.frequency,
      startTimeStr: startTime,
      startIntervalMin: interval,
      startOffsetMin: parseTimeToMinutes(startTime),
      durationMin: event.durationMin || 15,
      rewardTag: event.rewardTag,
      description: event.description || '',
      enabled: event.enabled !== false
    });
    this.isEventModalOpen.set(true);
  }

  closeEventModal(): void {
    this.isEventModalOpen.set(false);
    this.editingEventId.set(null);
  }

  saveEventModal(): void {
    if (this.eventForm.invalid) {
      this.showToast('Por favor, preencha todos os campos obrigatórios do evento.');
      return;
    }

    const raw = this.eventForm.getRawValue();
    const startTimeStr = raw.startTimeStr || '00:00';
    const startIntervalMin = Number(raw.startIntervalMin) || 120;
    const startOffsetMin = parseTimeToMinutes(startTimeStr);
    const durationMin = Number(raw.durationMin) || 15;
    const calculatedTimes = computeDailySchedule(startTimeStr, startIntervalMin);

    let frequency = raw.frequency?.trim();
    if (!frequency || frequency.includes('Minuto ')) {
      const hours = startIntervalMin / 60;
      if (startIntervalMin === 1440) {
        frequency = `Diário às ${startTimeStr}`;
      } else if (Number.isInteger(hours) && hours >= 1) {
        const sample = calculatedTimes.slice(0, 4).join(', ') + (calculatedTimes.length > 4 ? '...' : '');
        frequency = `A cada ${hours}h (${sample})`;
      } else {
        const sample = calculatedTimes.slice(0, 4).join(', ') + (calculatedTimes.length > 4 ? '...' : '');
        frequency = `A cada ${startIntervalMin}m (${sample})`;
      }
    }

    const currentList = this.serverEventsList();
    const editingId = this.editingEventId();

    if (editingId) {
      // Update existing event
      const updatedList = currentList.map(item => {
        if (item.id === editingId) {
          return {
            ...item,
            name: raw.name.trim(),
            category: raw.category,
            icon: raw.icon,
            colorTheme: raw.colorTheme,
            location: raw.location.trim(),
            frequency,
            startTimeStr,
            startIntervalMin,
            startOffsetMin,
            durationMin,
            rewardTag: raw.rewardTag.trim(),
            description: raw.description?.trim(),
            enabled: raw.enabled
          };
        }
        return item;
      });

      this.saveEventsToApi(updatedList);
      this.showToast(`Evento "${raw.name}" atualizado com sucesso!`);
    } else {
      // Create new custom event
      const newId = `custom-event-${Date.now()}`;
      const newEvent: ServerScheduleEvent = {
        id: newId,
        name: raw.name.trim(),
        category: raw.category,
        icon: raw.icon,
        colorTheme: raw.colorTheme,
        location: raw.location.trim(),
        frequency,
        startTimeStr,
        startIntervalMin,
        startOffsetMin,
        durationMin,
        rewardTag: raw.rewardTag.trim(),
        description: raw.description?.trim(),
        enabled: raw.enabled
      };

      const updatedList = [newEvent, ...currentList];
      this.saveEventsToApi(updatedList);
      this.showToast(`Novo evento "${raw.name}" criado e sincronizado!`);
    }

    this.closeEventModal();
  }

  toggleEventEnabled(eventId: string, $event?: Event): void {
    if ($event) {
      $event.stopPropagation();
    }
    const currentList = this.serverEventsList();
    const updatedList = currentList.map(item => {
      if (item.id === eventId) {
        const isEnabled = item.enabled !== false;
        return { ...item, enabled: !isEnabled };
      }
      return item;
    });

    this.saveEventsToApi(updatedList);
    const target = updatedList.find(e => e.id === eventId);
    if (target) {
      this.showToast(`Evento "${target.name}" ${target.enabled ? 'ativado' : 'desativado'} com sucesso.`);
    }
  }

  deleteEvent(eventId: string, $event?: Event): void {
    if ($event) {
      $event.stopPropagation();
    }
    const currentList = this.serverEventsList();
    const target = currentList.find(e => e.id === eventId);
    const updatedList = currentList.filter(e => e.id !== eventId);
    this.saveEventsToApi(updatedList);
    this.showToast(`Evento "${target?.name || 'Evento'}" excluído.`);
  }

  restoreDefaultEvents(): void {
    this.saveEventsToApi(DEFAULT_SERVER_EVENTS);
    this.showToast('Eventos padrão do MU FREE restaurados com sucesso!');
  }

  resetEventsToDefault(): void {
    this.restoreDefaultEvents();
  }

  onSearchInput(event: Event): void {
    const target = event.target as HTMLInputElement | null;
    if (target) {
      this.eventSearchQuery.set(target.value);
    }
  }

  forceStartEvent(eventId: string, $event?: Event): void {
    if ($event) {
      $event.stopPropagation();
    }
    const now = new Date();
    const currentHHMM = formatMinutesToTime(now.getHours() * 60 + now.getMinutes());

    const currentList = this.serverEventsList();
    const updatedList = currentList.map(item => {
      if (item.id === eventId) {
        return {
          ...item,
          startTimeStr: currentHHMM,
          startOffsetMin: parseTimeToMinutes(currentHHMM),
          enabled: true
        };
      }
      return item;
    });

    this.saveEventsToApi(updatedList);
    const target = updatedList.find(e => e.id === eventId);
    this.showToast(`Evento "${target?.name}" iniciado agora para teste ao vivo!`);
  }

  selectIconPreset(icon: string): void {
    this.eventForm.patchValue({ icon });
  }

  selectColorPreset(color: string): void {
    this.eventForm.patchValue({ colorTheme: color });
  }

  selectStartTimePreset(timeStr: string): void {
    this.eventForm.patchValue({ startTimeStr: timeStr, startOffsetMin: parseTimeToMinutes(timeStr) });
    this.modalCurrentStartTime.set(timeStr);
    this.updateFrequencyHint();
  }

  onStartTimeInput(event: Event): void {
    const target = event.target as HTMLInputElement | null;
    if (target && target.value) {
      this.eventForm.patchValue({ startTimeStr: target.value, startOffsetMin: parseTimeToMinutes(target.value) });
      this.modalCurrentStartTime.set(target.value);
      this.updateFrequencyHint();
    }
  }

  selectIntervalPreset(min: number): void {
    this.eventForm.patchValue({ startIntervalMin: min });
    this.modalCurrentInterval.set(min);
    this.updateFrequencyHint();
  }

  onIntervalInput(event: Event): void {
    const target = event.target as HTMLInputElement | null;
    if (target && target.value) {
      const val = Math.max(10, parseInt(target.value, 10) || 120);
      this.modalCurrentInterval.set(val);
      this.updateFrequencyHint();
    }
  }

  selectDurationPreset(min: number): void {
    this.eventForm.patchValue({ durationMin: min });
  }

  private updateFrequencyHint(): void {
    const startTime = this.eventForm.get('startTimeStr')?.value || '00:00';
    const interval = Number(this.eventForm.get('startIntervalMin')?.value || 120);
    const timesList = computeDailySchedule(startTime, interval);
    const hours = interval / 60;
    let label = '';
    if (interval === 1440) {
      label = `Diário às ${startTime}`;
    } else if (Number.isInteger(hours) && hours >= 1) {
      const sample = timesList.slice(0, 4).join(', ') + (timesList.length > 4 ? '...' : '');
      label = `A cada ${hours}h (${sample})`;
    } else {
      const sample = timesList.slice(0, 4).join(', ') + (timesList.length > 4 ? '...' : '');
      label = `A cada ${interval}m (${sample})`;
    }
    this.eventForm.patchValue({ frequency: label });
  }

  private async saveEventsToApi(events: ServerScheduleEvent[]): Promise<void> {
    this.serverEventsList.set(events);
    if (this.isBrowser) {
      try {
        await fetch('/api/events/config', {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          credentials: 'same-origin',
          body: JSON.stringify({ events })
        });
      } catch {
        // ignore
      }
    }
  }

  showToast(msg: string): void {
    this.showSuccessToast.set(msg);
    setTimeout(() => {
      this.showSuccessToast.set(null);
    }, 3500);
  }
}
