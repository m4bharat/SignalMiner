import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom, forkJoin } from 'rxjs';
import {
  buildSendEmailConfirmation,
  buildTestEmailConfirmation,
  CUSTOM_SIGNATURE_WRAPPER,
  DEFAULT_EMAIL_TEMPLATE,
  EMAIL_TEMPLATE_ASSET_PATHS,
  FALLBACK_EMAIL_LAYOUT_HTML,
  FALLBACK_ZEXTRI_SIGNATURE_HTML,
  LEGACY_ZEXTRI_SIGNATURE_MARKERS,
  SIGNATURE_TEXT_MARKERS,
  ZEXTRI_EMAIL_CONFIG
} from './email-template-config';
import { EmailStrings, EmailTemplateId, EmailTemplateStrings } from './email-strings';

type ContactStatus = 'NotContacted' | 'ReadyForManualOutreach' | 'Contacted' | 'Replied' | 'NotInterested' | 'DoNotContact';
type LeadStatus = 'New' | 'Enriched' | 'NeedsManualReview' | 'Qualified' | 'Disqualified' | 'Archived';
type DiscoverySource = 'GitHub' | 'X' | 'LinkedIn';
type SourceKind = 'GitHub' | 'Website' | 'X' | 'LinkedInProfileUrlOnly' | 'Manual';
type SourceFilter = '' | 'Imported' | SourceKind;
type DetailTab = 'overview' | 'outreach' | 'email';
type ToastKind = 'success' | 'error' | 'info';

interface EmailPreview {
  recipient: string;
  sender: string;
  subject: string;
  bodyHtml: string;
  bodyText: string;
  warnings: string[];
  isTest: boolean;
  templateName: string;
  templateCategory: string;
  templateVersion: string;
}

interface EmailTemplateMetadata {
  id: string;
  stringsKey?: string;
  name: string;
  description: string;
  category: string;
  version: string;
  intendedUse: string;
  defaultSubject: string;
  htmlPath: string;
  supportedVariables: string[];
  requiredVariables: string[];
}

interface SharedEmailTemplateParts {
  header: string;
  signature: string;
  footer: string;
  layout: string;
}

interface Lead {
  firstName?: string;
  lastName?: string;
  rank?: number;
  priorityGroup?: string;
  outreachFitScore?: number;
  zextriSegment?: string;
  countryUnverified?: string;
  personalizationAngle?: string;
  outreachScoreRationale?: string;
  dataQualityFlags?: string;
  recommendedAction?: string;
  originalFitScore?: number;
  sourceRow?: number;
  sourceSheet?: string;
  id: string;
  displayName: string;
  roleTitle?: string;
  publicEmail?: string;
  websiteUrl?: string;
  gitHubUrl?: string;
  xUrl?: string;
  linkedInUrl?: string;
  fitScore: number;
  scoreRationale: string;
  status: LeadStatus;
  contactStatus: ContactStatus;
  notes?: string;
  company?: { name: string; domain?: string; summary?: string };
  sourceProfiles?: SourceProfile[];
  emailLogs?: LeadEmailLog[];
}

interface LeadEmailLog {
  occurredAt: string;
  kind: string;
  template?: string;
  log: string;
  isTest: boolean;
}

interface SourceProfile {
  kind: string;
  url: string;
  publicHandle: string;
}

interface SearchResult {
  items: Lead[];
  total: number;
}

interface LeadImportResult {
  totalRows: number;
  validRows: number;
  importedRows: number;
  skippedRows: number;
  issues: LeadImportIssue[];
  previewRows: LeadImportPreviewRow[];
}

interface LeadImportIssue {
  sourceSheet?: string;
  rowNumber: number;
  field: string;
  message: string;
}

interface LeadImportPreviewRow {
  rank?: number;
  priorityGroup?: string;
  outreachFitScore?: number;
  dataQualityFlags?: string;
  recommendedAction?: string;
  sourceSheet?: string;
  rowNumber: number;
  displayName: string;
  company?: string;
  publicEmail?: string;
  fitScore: number;
  isDuplicate: boolean;
  duplicateReason?: string;
}

interface ManualContactForm {
  name: string;
  email: string;
  title: string;
  company: string;
  companyDomain: string;
  linkedInUrl: string;
  note: string;
}

interface ToastMessage {
  kind: ToastKind;
  title: string;
  body: string;
}

@Component({
  selector: 'sm-root',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './app.component.html'
})
export class AppComponent {
  private readonly http = inject(HttpClient);
  private readonly apiBase = 'http://localhost:5000/api';
  private readonly authStorageKey = 'signalminer.dashboard.authenticated';
  private readonly dashboardUsername = 'admin@zextri.com';
  private readonly dashboardPassword = 'zextri';
  private toastTimer: number | undefined;
  private sharedEmailTemplateParts: SharedEmailTemplateParts | null = null;
  private templateHtmlCache = new Map<string, string>();
  protected readonly emailStrings = EmailStrings;

  protected readonly isAuthenticated = signal(false);
  protected readonly loginEmail = signal('');
  protected readonly loginPassword = signal('');
  protected readonly loginError = signal('');
  protected readonly leads = signal<Lead[]>([]);
  protected readonly selectedLeadIds = signal<ReadonlySet<string>>(new Set<string>());
  protected readonly selectedLead = signal<Lead | null>(null);
  protected readonly total = signal(0);
  protected readonly query = signal('');
  protected readonly minFitScore = signal<number | null>(null);
  protected readonly priorityGroup = signal('');
  protected readonly segment = signal('');
  protected readonly country = signal('');
  protected readonly companyFilter = signal('');
  protected readonly minOutreachScore = signal<number | null>(null);
  protected readonly maxOutreachScore = signal<number | null>(null);
  protected readonly hasEmail = signal('');
  protected readonly hasLinkedIn = signal('');
  protected readonly hasWebsite = signal('');
  protected readonly sortBy = signal('rank');
  protected readonly contactStatus = signal<ContactStatus | ''>('');
  protected readonly sourceFilter = signal<SourceFilter>('');
  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);
  protected readonly discoveryQuery = signal('founder saas ai');
  protected readonly discoverySource = signal<DiscoverySource>('GitHub');
  protected readonly loading = signal(false);
  protected readonly message = signal('Manual-review-first lead discovery workspace');
  protected readonly outreachNote = signal('');
  protected readonly emailTo = signal('');
  protected readonly emailSubject = signal('');
  protected readonly emailBodyHtml = signal('');
  protected readonly emailBodyEditorHtml = signal('');
  protected readonly emailAttachments = signal<File[]>([]);
  protected readonly emailReplyTo = signal('');
  protected readonly emailCc = signal('');
  protected readonly emailBcc = signal('');
  protected readonly emailSignatureHtml = signal('');
  protected readonly emailSignatureEditorHtml = signal('');
  protected readonly includeSignature = signal(true);
  protected readonly emailSending = signal(false);
  protected readonly draftDirty = signal(false);
  protected readonly activeEmailLeadId = signal<string | null>(null);
  protected readonly emailPreview = signal<EmailPreview | null>(null);
  protected readonly emailTemplates = signal<EmailTemplateMetadata[]>([]);
  protected readonly selectedEmailTemplateId = signal('');
  protected readonly selectedEmailTemplateVersion = signal('');
  protected readonly selectedEmailTemplateName = signal('');
  private readonly activeEmailTemplateHtml = signal('');
  protected readonly importFile = signal<File | null>(null);
  protected readonly importResult = signal<LeadImportResult | null>(null);
  protected readonly manualContact = signal<ManualContactForm>({
    name: '',
    email: '',
    title: '',
    company: '',
    companyDomain: '',
    linkedInUrl: '',
    note: ''
  });
  protected readonly detailTab = signal<DetailTab>('overview');
  protected readonly detailExpanded = signal(true);
  protected readonly expandedEmailLogKeys = signal<ReadonlySet<string>>(new Set<string>());
  protected readonly toast = signal<ToastMessage | null>(null);

  protected readonly totalPages = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize())));
  protected readonly selectableEmailLeads = computed(() =>
    this.leads().filter(lead => this.canEmailLead(lead)));
  protected readonly selectedEmailLeads = computed(() =>
    this.selectableEmailLeads().filter(lead => this.selectedLeadIds().has(lead.id)));
  protected readonly selectedEmailLeadCount = computed(() => this.selectedEmailLeads().length);
  protected readonly allPageEmailLeadsSelected = computed(() => {
    const selectableLeads = this.selectableEmailLeads();
    return selectableLeads.length > 0 &&
      selectableLeads.every(lead => this.selectedLeadIds().has(lead.id));
  });
  protected readonly somePageEmailLeadsSelected = computed(() =>
    this.selectedEmailLeadCount() > 0 && !this.allPageEmailLeadsSelected());

  protected readonly pageStart = computed(() => {
    if (this.total() === 0) return 0;
    return (this.page() - 1) * this.pageSize() + 1;
  });

  protected readonly pageEnd = computed(() => Math.min(this.total(), this.page() * this.pageSize()));

  constructor() {
    if (sessionStorage.getItem(this.authStorageKey) !== 'true') {
      return;
    }

    this.isAuthenticated.set(true);
    this.initializeDashboard();
  }

  protected login(): void {
    const email = this.loginEmail().trim().toLowerCase();
    const password = this.loginPassword();
    if (email !== this.dashboardUsername || password !== this.dashboardPassword) {
      this.loginError.set('Invalid email or password.');
      return;
    }

    sessionStorage.setItem(this.authStorageKey, 'true');
    this.loginPassword.set('');
    this.loginError.set('');
    this.isAuthenticated.set(true);
    this.initializeDashboard();
  }

  protected logout(): void {
    sessionStorage.removeItem(this.authStorageKey);
    this.isAuthenticated.set(false);
    this.leads.set([]);
    this.selectedLead.set(null);
    this.total.set(0);
    this.emailPreview.set(null);
    this.message.set('Manual-review-first lead discovery workspace');
  }

  private initializeDashboard(): void {
    this.loadEmailSettings();
    this.loadEmailTemplates();
    this.search();
  }

  protected search(resetPage = true): void {
    const min = this.minOutreachScore();
    const max = this.maxOutreachScore();
    if ([min, max].some(value => value !== null && (!Number.isFinite(value) || value < 0 || value > 10)) ||
        (min !== null && max !== null && min > max)) {
      this.message.set('Outreach scores must be between 0 and 10, with minimum no greater than maximum.');
      return;
    }
    if (resetPage) {
      this.page.set(1);
    }

    this.loading.set(true);
    let params = new HttpParams()
      .set('page', this.page())
      .set('pageSize', this.pageSize());

    if (this.query()) params = params.set('query', this.query());
    for (const [key, value] of Object.entries({
      priorityGroup: this.priorityGroup().trim(), segment: this.segment().trim(),
      country: this.country().trim(), company: this.companyFilter().trim(),
      hasEmail: this.hasEmail(), hasLinkedIn: this.hasLinkedIn(), hasWebsite: this.hasWebsite(),
      sortBy: this.sortBy()
    })) {
      if (value) params = params.set(key, value);
    }
    if (min !== null) params = params.set('minOutreachScore', min);
    if (max !== null) params = params.set('maxOutreachScore', max);
    if (this.minFitScore() !== null) params = params.set('minFitScore', this.minFitScore()!.toString());
    if (this.contactStatus()) params = params.set('contactStatus', this.contactStatus());
    if (this.sourceFilter() === 'Imported') {
      params = params.set('importedOnly', 'true');
    } else if (this.sourceFilter()) {
      params = params.set('sourceKind', this.sourceFilter());
    }

    this.http.get<SearchResult>(`${this.apiBase}/leads/search`, { params }).subscribe({
      next: result => {
        this.leads.set(result.items);
        this.total.set(result.total);
        this.pruneSelectedLeadIds(result.items);
        const firstLead = result.items[0] ?? null;
        if (firstLead) {
          this.prepareLeadEmail(firstLead);
        } else {
          this.selectedLead.set(null);
          this.emailTo.set('');
        }
        if (this.page() > this.totalPages()) {
          this.page.set(this.totalPages());
          this.search(false);
          return;
        }
        this.loading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.message.set(error.error?.message || 'Could not load leads. Check that the API is running on http://localhost:5000.');
        this.loading.set(false);
      }
    });
  }

  protected discover(): void {
    this.loading.set(true);
    this.http.post<Lead[]>(`${this.apiBase}/leads/discover`, {
      query: this.discoveryQuery(),
      limit: 25,
      source: this.discoverySource()
    }).subscribe({
      next: leads => {
        this.message.set(`Discovered ${leads.length} ${this.discoverySource()} leads for manual review.`);
        this.showDiscoveredLeads();
        this.leads.set(leads);
        this.total.set(leads.length);
        this.pruneSelectedLeadIds(leads);
        const firstLead = leads[0] ?? null;
        if (firstLead) {
          this.prepareLeadEmail(firstLead);
        } else {
          this.selectedLead.set(null);
          this.emailTo.set('');
        }
        this.loading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.message.set(error.error?.message || 'Discovery failed. Check API, PostgreSQL, and source rate limits.');
        this.loading.set(false);
      }
    });
  }

  private showDiscoveredLeads(): void {
    this.resetAdvancedFilters();
    this.query.set('');
    this.minFitScore.set(null);
    this.contactStatus.set('');
    this.sourceFilter.set(this.discoverySourceFilter());
    this.page.set(1);
  }

  private discoverySourceFilter(): SourceFilter {
    switch (this.discoverySource()) {
      case 'GitHub':
        return 'GitHub';
      case 'X':
        return 'X';
      case 'LinkedIn':
        return 'LinkedInProfileUrlOnly';
    }
  }

  protected selectImportFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    this.importFile.set(file);
    this.importResult.set(null);
    if (file) {
      this.message.set(`Ready to preview ${file.name}.`);
    }
  }

  protected previewImport(): void {
    this.uploadImport(false);
  }

  protected commitImport(): void {
    this.uploadImport(true);
  }

  protected canAddManualContact(): boolean {
    const contact = this.manualContact();
    return contact.name.trim().length > 0 &&
      (contact.email.trim().length > 0 || contact.linkedInUrl.trim().length > 0);
  }

  protected updateManualContact(field: keyof ManualContactForm, value: string): void {
    this.manualContact.update(contact => ({ ...contact, [field]: value }));
  }

  protected addManualContact(): void {
    if (!this.canAddManualContact()) {
      this.message.set('Enter a contact name and either an email or LinkedIn URL.');
      return;
    }

    const file = this.buildManualContactFile(this.manualContact());
    this.submitImport(file, true, 'manual');
  }

  protected primarySource(lead: Lead): { label: string; url?: string } {
    if (lead.gitHubUrl) return { label: 'GitHub', url: lead.gitHubUrl };
    if (lead.xUrl) return { label: 'X', url: lead.xUrl };
    if (lead.linkedInUrl) return { label: 'LinkedIn', url: lead.linkedInUrl };
    if (lead.websiteUrl) return { label: 'Website', url: lead.websiteUrl };
    return { label: 'None' };
  }

  protected clearFilters(): void {
    this.resetAdvancedFilters();
    this.query.set('');
    this.minFitScore.set(null);
    this.contactStatus.set('');
    this.sourceFilter.set('');
    this.search();
  }

  private resetAdvancedFilters(): void {
    this.priorityGroup.set('');
    this.segment.set('');
    this.country.set('');
    this.companyFilter.set('');
    this.minOutreachScore.set(null);
    this.maxOutreachScore.set(null);
    this.hasEmail.set('');
    this.hasLinkedIn.set('');
    this.hasWebsite.set('');
    this.sortBy.set('rank');
  }

  protected toggleDetailExpanded(): void {
    this.detailExpanded.update(value => !value);
  }

  protected emailLogKey(entry: LeadEmailLog, index: number): string {
    return `${entry.occurredAt}|${entry.kind}|${entry.template ?? ''}|${entry.log.length}|${index}`;
  }

  protected isEmailLogExpanded(entry: LeadEmailLog, index: number): boolean {
    return this.expandedEmailLogKeys().has(this.emailLogKey(entry, index));
  }

  protected toggleEmailLog(entry: LeadEmailLog, index: number): void {
    const key = this.emailLogKey(entry, index);
    this.expandedEmailLogKeys.update(keys => {
      const next = new Set(keys);
      if (next.has(key)) {
        next.delete(key);
      } else {
        next.add(key);
      }

      return next;
    });
  }

  protected nextPage(): void {
    if (this.page() >= this.totalPages()) return;
    this.page.update(value => value + 1);
    this.search(false);
  }

  protected previousPage(): void {
    if (this.page() <= 1) return;
    this.page.update(value => value - 1);
    this.search(false);
  }

  protected changePageSize(value: number): void {
    this.pageSize.set(value);
    this.search();
  }

  protected enrich(lead: Lead): void {
    this.loading.set(true);
    this.http.post<Lead>(`${this.apiBase}/leads/${lead.id}/enrich`, {}).subscribe({
      next: updated => {
        this.selectedLead.set(updated);
        this.message.set('Lead enriched from public website data.');
        this.search();
      },
      error: () => {
        this.message.set('Enrichment failed or no crawlable public website was available.');
        this.loading.set(false);
      }
    });
  }

  protected selectLead(lead: Lead): void {
    if (this.draftDirty() && this.activeEmailLeadId() && this.activeEmailLeadId() !== lead.id) {
      const shouldSwitch = window.confirm(EmailStrings.ui.confirms.unsavedDraftSwitch);
      if (!shouldSwitch) {
        return;
      }
    }

    this.prepareLeadEmail(lead);
  }

  protected toggleLeadSelection(lead: Lead, event: Event): void {
    event.stopPropagation();
    if (!this.canEmailLead(lead)) return;
    this.selectedLeadIds.update(ids => {
      const next = new Set(ids);
      if (next.has(lead.id)) {
        next.delete(lead.id);
      } else {
        next.add(lead.id);
      }

      return next;
    });
  }

  protected togglePageLeadSelection(event: Event): void {
    event.stopPropagation();
    const shouldSelectAll = !this.allPageEmailLeadsSelected();
    this.selectedLeadIds.update(ids => {
      const next = new Set(ids);
      for (const lead of this.selectableEmailLeads()) {
        if (shouldSelectAll) {
          next.add(lead.id);
        } else {
          next.delete(lead.id);
        }
      }

      return next;
    });
  }

  protected isLeadSelectedForEmail(lead: Lead): boolean {
    return this.selectedLeadIds().has(lead.id);
  }

  protected canSendSelectedLeads(): boolean {
    return this.selectedEmailLeadCount() > 0 && !this.emailSending();
  }

  protected canSendPrimaryEmail(): boolean {
    return this.selectedEmailLeadCount() > 0
      ? this.canSendSelectedLeads()
      : this.canSendSelectedLead();
  }

  protected primaryEmailButtonLabel(): string {
    const selectedCount = this.selectedEmailLeadCount();
    return selectedCount > 0
      ? `${EmailStrings.ui.buttons.sendSelectedEmail} (${selectedCount})`
      : EmailStrings.ui.buttons.sendEmail;
  }

  private prepareLeadEmail(lead: Lead): void {
    this.selectedLead.set(lead);
    this.activeEmailLeadId.set(lead.id);
    this.detailTab.set('overview');
    this.expandedEmailLogKeys.set(new Set<string>());
    this.outreachNote.set('');
    this.emailTo.set(lead.publicEmail ?? '');
    this.emailReplyTo.set(ZEXTRI_EMAIL_CONFIG.senderEmail);
    this.emailAttachments.set([]);
    this.emailPreview.set(null);
    this.draftDirty.set(false);
    const templateId = this.selectedEmailTemplateId() || this.emailTemplates()[0]?.id;
    if (templateId) {
      this.applyEmailTemplate(templateId, false);
    } else {
      this.emailSubject.set(DEFAULT_EMAIL_TEMPLATE.subject);
      this.setEmailBodyHtml(this.resolveDraftHtmlForEditor(this.composeEmailTemplateHtml(this.defaultEmailCopy())));
      this.selectedEmailTemplateName.set(DEFAULT_EMAIL_TEMPLATE.name);
      this.selectedEmailTemplateVersion.set(DEFAULT_EMAIL_TEMPLATE.version);
    }
  }

  protected selectEmailTemplate(templateId: string): void {
    this.applyEmailTemplate(templateId, true);
  }

  protected resetToTemplate(): void {
    const template = this.selectedOrDefaultTemplate();
    if (!template) {
      this.showToast('error', EmailStrings.ui.toasts.templatesLoadingTitle, EmailStrings.ui.toasts.templatesLoadingBody);
      return;
    }

    this.applyEmailTemplate(template.id, false);
  }

  protected updateEmailBody(event: Event): void {
    this.setEmailBodyHtml((event.target as HTMLElement).innerHTML, false);
    this.markDraftDirty();
  }

  protected updateEmailSignature(event: Event): void {
    this.setEmailSignatureHtml((event.target as HTMLElement).innerHTML, false);
    if (this.includeSignature()) {
      this.setEmailBodyHtml(this.withDefaultSignature(this.emailBodyHtml()));
    }
    this.markDraftDirty();
  }

  protected setIncludeSignature(value: boolean): void {
    this.includeSignature.set(value);
    this.setEmailBodyHtml(value
      ? this.withDefaultSignature(this.emailBodyHtml())
      : this.removeSignature(this.emailBodyHtml()));
    this.markDraftDirty();
  }

  protected formatEmail(command: string, value?: string): void {
    document.execCommand(command, false, value);
  }

  protected addEmailLink(): void {
    const url = window.prompt(EmailStrings.ui.toolbar.addLinkPrompt);
    if (!url) return;

    const normalizedUrl = url.startsWith('http') ? url : `https://${url}`;
    this.formatEmail('createLink', normalizedUrl);
  }

  protected selectEmailAttachments(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.emailAttachments.set(Array.from(input.files ?? []));
    this.markDraftDirty();
  }

  protected saveEmailSettings(): void {
    const settings = {
      replyTo: this.emailReplyTo().trim(),
      cc: this.emailCc().trim(),
      bcc: this.emailBcc().trim(),
      signatureHtml: this.emailSignatureHtml().trim(),
      includeSignature: this.includeSignature()
    };

    localStorage.setItem('signalminer.emailSettings', JSON.stringify(settings));
    this.message.set(EmailStrings.ui.messages.emailSettingsSaved);
  }

  protected insertSignature(): void {
    const signatureHtml = this.signatureHtml();
    if (!signatureHtml) {
      this.message.set(EmailStrings.ui.messages.addSignatureFirst);
      return;
    }

    this.setEmailBodyHtml(this.withDefaultSignature(this.emailBodyHtml()));
    this.markDraftDirty();
  }

  protected useZextriSignature(): void {
    this.setEmailSignatureHtml(this.zextriSignatureHtml());
    this.includeSignature.set(true);
    this.setEmailBodyHtml(this.withDefaultSignature(this.removeSignature(this.emailBodyHtml())));
    this.message.set(EmailStrings.ui.messages.signatureApplied);
    this.markDraftDirty();
  }

  protected updateEmailTo(value: string): void {
    this.emailTo.set(value);
    this.markDraftDirty();
  }

  protected updateEmailSubject(value: string): void {
    this.emailSubject.set(value);
    this.markDraftDirty();
  }

  protected previewEmail(isTest = false): void {
    const preview = this.buildEmailPreview(isTest);
    if (!preview) {
      return;
    }

    this.emailPreview.set(preview);
  }

  protected regenerateOpening(): void {
    const lead = this.selectedLead();
    if (!lead) {
      return;
    }

    const firstName = this.firstName(lead);
    const company = lead.company?.name?.trim();
    const opening = firstName && company
      ? DEFAULT_EMAIL_TEMPLATE.body.split('\n\n').slice(0, 2).join('\n\n')
      : DEFAULT_EMAIL_TEMPLATE.fallbackOpening.replace('there', firstName || 'there');
    const paragraphs = this.htmlToText(this.removeSignature(this.emailBodyHtml()))
      .split(/\n{2,}/)
      .map(part => part.trim())
      .filter(Boolean);
    const rest = paragraphs.slice(2).join('\n\n') || DEFAULT_EMAIL_TEMPLATE.fallbackBodyRest;

    this.setEmailBodyHtml(this.withDefaultSignature(this.textToHtml(`${opening}\n\n${rest}`)));
    this.markDraftDirty();
    this.previewEmail();
  }

  protected sendTestEmail(): void {
    const preview = this.buildEmailPreview(true);
    if (!preview) {
      return;
    }

    this.emailPreview.set(preview);
    const confirmed = window.confirm(buildTestEmailConfirmation({
      leadName: this.selectedLead()?.displayName ?? EmailStrings.ui.empty.noLeadSelected,
      recipient: preview.recipient,
      sender: preview.sender,
      templateName: preview.templateName,
      templateVersion: preview.templateVersion,
      subject: preview.subject,
      bodyText: preview.bodyText
    }));
    if (!confirmed) {
      return;
    }

    this.submitEmail(preview);
  }

  protected sendEmail(): void {
    const preview = this.buildEmailPreview(false);
    if (!preview) {
      return;
    }

    this.emailPreview.set(preview);
    const confirmed = window.confirm(buildSendEmailConfirmation({
      leadSummary: this.leadSummary(this.selectedLead()),
      recipient: preview.recipient,
      sender: preview.sender,
      templateName: preview.templateName,
      templateVersion: preview.templateVersion,
      subject: preview.subject,
      bodyText: preview.bodyText
    }));
    if (!confirmed) {
      return;
    }

    this.submitEmail(preview);
  }

  protected sendPrimaryEmailAction(): void {
    if (this.selectedEmailLeadCount() > 0) {
      void this.sendSelectedEmails();
      return;
    }

    this.sendEmail();
  }

  protected async sendSelectedEmails(): Promise<void> {
    if (this.emailSending()) return;

    const leads = this.selectedEmailLeads();
    if (leads.length === 0) {
      this.showToast('error', EmailStrings.ui.toasts.emailNotReadyTitle, EmailStrings.ui.toasts.selectBulkContactsBody);
      return;
    }

    const previews: Array<{ lead: Lead; preview: EmailPreview }> = [];
    for (const lead of leads) {
      const preview = this.buildEmailPreview(false, true, lead);
      if (!preview) {
        return;
      }

      previews.push({ lead, preview });
    }

    const confirmed = window.confirm(buildSendEmailConfirmation({
      leadSummary: `${previews.length} selected contacts`,
      recipient: `${previews.length} separate emails: ${previews.map(item => item.preview.recipient).join(', ')}`,
      sender: previews[0].preview.sender,
      templateName: previews[0].preview.templateName,
      templateVersion: previews[0].preview.templateVersion,
      subject: previews[0].preview.subject,
      bodyText: previews[0].preview.bodyText
    }));
    if (!confirmed) {
      return;
    }

    this.loading.set(true);
    this.emailSending.set(true);

    const failures: string[] = [];
    for (const item of previews) {
      try {
        const updated = await firstValueFrom(this.postEmail(item.lead, item.preview));
        if (this.selectedLead()?.id === updated.id) {
          this.selectedLead.set(updated);
        }
      } catch (error) {
        failures.push(`${item.lead.displayName}: ${this.getErrorMessage(error as HttpErrorResponse, EmailStrings.ui.toasts.emailFailedBody)}`);
      }
    }

    this.loading.set(false);
    this.emailSending.set(false);

    if (failures.length > 0) {
      const message = `Sent ${previews.length - failures.length} of ${previews.length} selected emails. ${failures[0]}`;
      this.message.set(message);
      this.showToast('error', EmailStrings.ui.toasts.emailFailedTitle, message);
    } else {
      const message = `${EmailStrings.ui.messages.bulkEmailSubmittedPrefix} ${previews.length} contacts as separate emails.`;
      this.message.set(message);
      this.showToast('success', EmailStrings.ui.toasts.emailSentTitle, message);
      this.selectedLeadIds.set(new Set<string>());
      this.draftDirty.set(false);
    }

    this.search(false);
  }

  private submitEmail(preview: EmailPreview): void {
    const lead = this.selectedLead();
    if (!lead) return;

    if (this.emailSending()) return;

    this.loading.set(true);
    this.emailSending.set(true);
    this.postEmail(lead, preview).subscribe({
      next: updated => {
        this.selectedLead.set(updated);
        const successMessage = preview.isTest
          ? `${EmailStrings.ui.messages.testEmailSentPrefix} ${preview.recipient}.`
          : `${EmailStrings.ui.messages.emailSubmittedPrefix} ${preview.recipient}.`;
        this.message.set(successMessage);
        this.showToast('success', EmailStrings.ui.toasts.emailSentTitle, successMessage);
        this.loading.set(false);
        this.emailSending.set(false);
        if (!preview.isTest) {
          this.draftDirty.set(false);
        }
        this.search(false);
      },
      error: (error: HttpErrorResponse) => {
        const errorMessage = this.getErrorMessage(error, EmailStrings.ui.toasts.emailFailedBody);
        this.message.set(errorMessage);
        this.showToast('error', EmailStrings.ui.toasts.emailFailedTitle, errorMessage);
        this.loading.set(false);
        this.emailSending.set(false);
      }
    });
  }

  private postEmail(lead: Lead, preview: EmailPreview) {
    const form = new FormData();
    form.append('toEmail', preview.recipient);
    form.append('subject', preview.subject);
    form.append('body', preview.bodyText);
    form.append('bodyHtml', preview.bodyHtml);
    form.append('replyTo', ZEXTRI_EMAIL_CONFIG.senderEmail);
    if (this.emailCc().trim()) form.append('cc', this.emailCc().trim());
    if (this.emailBcc().trim()) form.append('bcc', this.emailBcc().trim());
    form.append('isTest', String(preview.isTest));
    form.append('templateId', this.selectedEmailTemplateId());
    form.append('templateVersion', preview.templateVersion);
    form.append('templateName', preview.templateName);
    form.append('templateCategory', preview.templateCategory);
    for (const file of this.emailAttachments()) {
      form.append('attachments', file, file.name);
    }

    return this.http.post<Lead>(`${this.apiBase}/leads/${lead.id}/email`, form);
  }

  protected dismissToast(): void {
    this.toast.set(null);
    if (this.toastTimer) {
      window.clearTimeout(this.toastTimer);
      this.toastTimer = undefined;
    }
  }

  private loadEmailTemplates(): void {
    forkJoin({
      manifest: this.http.get<EmailTemplateMetadata[]>(ZEXTRI_EMAIL_CONFIG.templateManifestUrl),
      header: this.http.get(EMAIL_TEMPLATE_ASSET_PATHS.sharedHeader, { responseType: 'text' }),
      signature: this.http.get(EMAIL_TEMPLATE_ASSET_PATHS.sharedSignature, { responseType: 'text' }),
      footer: this.http.get(EMAIL_TEMPLATE_ASSET_PATHS.sharedFooter, { responseType: 'text' }),
      layout: this.http.get(EMAIL_TEMPLATE_ASSET_PATHS.sharedLayout, { responseType: 'text' })
    }).subscribe({
      next: result => {
        const templates = result.manifest
          .map(template => this.withTemplateStrings(template))
          .filter(template => this.isValidTemplateMetadata(template));
        this.sharedEmailTemplateParts = {
          header: result.header,
          signature: result.signature,
          footer: result.footer,
          layout: result.layout
        };
        this.emailTemplates.set(templates);
        if (templates.length === 0) {
          this.showToast('error', EmailStrings.ui.toasts.templatesUnavailableTitle, EmailStrings.ui.toasts.noValidTemplatesBody);
          return;
        }

        const selectedTemplate = templates.find(template => template.id === this.selectedEmailTemplateId()) ?? templates[0];
        if (selectedTemplate) {
          this.selectedEmailTemplateId.set(selectedTemplate.id);
          this.selectedEmailTemplateName.set(selectedTemplate.name);
          this.selectedEmailTemplateVersion.set(selectedTemplate.version);
        }

        const lead = this.selectedLead();
        if (lead && selectedTemplate) {
          this.applyEmailTemplate(this.selectedEmailTemplateId(), false);
        }
      },
      error: () => this.showToast('error', EmailStrings.ui.toasts.templatesUnavailableTitle, EmailStrings.ui.toasts.templateAssetsUnavailableBody)
    });
  }

  private applyEmailTemplate(templateId: string, confirmReplace: boolean): void {
    const template = this.emailTemplates().find(item => item.id === templateId) ?? this.selectedOrDefaultTemplate();
    if (!template || !this.isValidTemplateMetadata(template)) {
      this.showToast('error', EmailStrings.ui.toasts.templatesUnavailableTitle, EmailStrings.ui.toasts.selectedTemplateInvalidBody);
      return;
    }

    if (confirmReplace && this.draftDirty()) {
      const shouldReplace = window.confirm(EmailStrings.ui.confirms.replaceDraft);
      if (!shouldReplace) {
        return;
      }
    }

    const cached = this.templateHtmlCache.get(template.id);
    if (cached) {
      this.applyLoadedTemplate(template, cached);
      return;
    }

    this.http.get(template.htmlPath, { responseType: 'text' }).subscribe({
      next: html => {
        this.templateHtmlCache.set(template.id, html);
        this.applyLoadedTemplate(template, html);
      },
      error: () => this.showToast('error', EmailStrings.ui.toasts.templatesUnavailableTitle, EmailStrings.ui.toasts.selectedTemplateLoadFailedBody)
    });
  }

  private applyLoadedTemplate(template: EmailTemplateMetadata, html: string): void {
    this.selectedEmailTemplateId.set(template.id);
    this.selectedEmailTemplateName.set(template.name);
    this.selectedEmailTemplateVersion.set(template.version);
    this.activeEmailTemplateHtml.set(html);
    this.emailSubject.set(template.defaultSubject);
    this.setEmailBodyHtml(this.resolveDraftHtmlForEditor(this.composeEmailTemplateHtml(html)));
    this.emailPreview.set(null);
    this.draftDirty.set(false);
  }

  private setEmailBodyHtml(value: string, updateEditor = true): void {
    this.emailBodyHtml.set(value);
    if (updateEditor) {
      this.emailBodyEditorHtml.set(value);
    }
  }

  private setEmailSignatureHtml(value: string, updateEditor = true): void {
    this.emailSignatureHtml.set(value);
    if (updateEditor) {
      this.emailSignatureEditorHtml.set(value);
    }
  }

  private resolveDraftHtmlForEditor(html: string): string {
    const lead = this.selectedLead();
    return lead ? this.resolveVariables(html, lead) : html;
  }

  private isValidTemplateMetadata(template: EmailTemplateMetadata): boolean {
    return /^[a-z0-9-]+$/.test(template.id) &&
      template.htmlPath === `assets/email-templates/${template.id}/template.html` &&
      Boolean(template.name?.trim()) &&
      Boolean(template.version?.trim()) &&
      Array.isArray(template.supportedVariables) &&
      Array.isArray(template.requiredVariables);
  }

  private withTemplateStrings(template: EmailTemplateMetadata): EmailTemplateMetadata {
    const strings = EmailTemplateStrings[template.id as EmailTemplateId];
    if (!strings) {
      return template;
    }

    return {
      ...template,
      stringsKey: template.stringsKey ?? template.id,
      name: strings.name,
      description: strings.description,
      category: strings.category,
      intendedUse: strings.intendedUse,
      defaultSubject: strings.defaultSubject
    };
  }

  private selectedOrDefaultTemplate(): EmailTemplateMetadata | null {
    const templates = this.emailTemplates();
    return templates.find(template => template.id === this.selectedEmailTemplateId()) ?? templates[0] ?? null;
  }

  private composeEmailTemplateHtml(templateHtml: string): string {
    const parts = this.sharedEmailTemplateParts;
    const content = templateHtml
      .replaceAll('{{sharedHeader}}', parts?.header ?? '')
      .replaceAll('{{sharedSignature}}', parts?.signature ?? this.zextriSignatureHtml())
      .replaceAll('{{sharedFooter}}', parts?.footer ?? '')
      .trim();

    return (parts?.layout ?? '{{emailContent}}').replace('{{emailContent}}', content);
  }

  protected selectedRecipientLabel(): string {
    const lead = this.selectedLead();
    if (!lead) {
      return EmailStrings.ui.empty.noLeadSelected;
    }

    return `${lead.displayName}${lead.company?.name ? ` at ${lead.company.name}` : ''} <${lead.publicEmail || EmailStrings.ui.empty.noEmail}>`;
  }

  private leadSummary(lead: Lead | null): string {
    if (!lead) {
      return EmailStrings.ui.empty.noLeadSelected;
    }

    return `${lead.displayName}${lead.company?.name ? ` at ${lead.company.name}` : ''}`;
  }

  protected canEmailLead(lead: Lead): boolean {
    return Boolean(lead.publicEmail) && lead.contactStatus !== 'DoNotContact';
  }

  protected canSendSelectedLead(): boolean {
    const lead = this.selectedLead();
    return Boolean(
      lead &&
      this.canEmailLead(lead) &&
      !this.emailSending() &&
      this.activeEmailLeadId() === lead.id &&
      this.isValidEmail(this.emailTo()) &&
      lead.publicEmail &&
      this.emailTo().trim().toLowerCase() === lead.publicEmail.trim().toLowerCase()
    );
  }

  protected composerWarnings(): string[] {
    const preview = this.buildEmailPreview(false, false);
    return preview?.warnings ?? this.getDraftWarnings(this.selectedLead());
  }

  private buildEmailPreview(isTest: boolean, showFeedback = true, previewLead?: Lead): EmailPreview | null {
    const lead = previewLead ?? this.selectedLead();
    if (!lead) {
      if (showFeedback) this.showToast('error', EmailStrings.ui.toasts.emailNotReadyTitle, EmailStrings.ui.toasts.selectLeadBody);
      return null;
    }

    if (!previewLead && this.activeEmailLeadId() !== lead.id) {
      if (showFeedback) this.showToast('error', EmailStrings.ui.toasts.emailNotReadyTitle, EmailStrings.ui.toasts.draftBelongsToAnotherLeadBody);
      return null;
    }

    const leadEmail = lead.publicEmail?.trim() ?? '';
    const requestedRecipient = isTest
      ? ZEXTRI_EMAIL_CONFIG.senderEmail
      : (previewLead ? leadEmail : this.emailTo().trim());
    if (!this.isValidEmail(requestedRecipient)) {
      if (showFeedback) this.showToast('error', EmailStrings.ui.toasts.emailNotReadyTitle, EmailStrings.ui.toasts.validRecipientBody);
      return null;
    }

    if (!isTest) {
      if (!leadEmail) {
        if (showFeedback) this.showToast('error', EmailStrings.ui.toasts.emailNotReadyTitle, EmailStrings.ui.toasts.missingLeadEmailBody);
        return null;
      }

      if (requestedRecipient.toLowerCase() !== leadEmail.toLowerCase()) {
        if (showFeedback) this.showToast('error', EmailStrings.ui.toasts.emailNotReadyTitle, EmailStrings.ui.toasts.recipientMismatchBody);
        return null;
      }
    }

    const subject = this.resolveVariables(this.emailSubject(), lead, false).trim();
    const bodyHtml = this.finalEmailBodyHtml(lead, this.bulkTemplateBodyHtml(previewLead));
    const bodyText = this.htmlToText(bodyHtml);
    const unresolvedVariables = this.findUnresolvedVariables(`${subject}\n${bodyText}`);
    const missingRequiredVariables = this.getMissingRequiredVariables(lead);
    if (!subject || !bodyText) {
      if (showFeedback) this.showToast('error', EmailStrings.ui.toasts.emailNotReadyTitle, EmailStrings.ui.toasts.subjectAndBodyRequiredBody);
      return null;
    }

    if (unresolvedVariables.length > 0 || missingRequiredVariables.length > 0) {
      if (showFeedback) this.showToast('error', EmailStrings.ui.toasts.emailNotReadyTitle, EmailStrings.ui.toasts.requiredVariablesBody);
      return null;
    }

    return {
      recipient: requestedRecipient,
      sender: `${ZEXTRI_EMAIL_CONFIG.senderDisplayName} <${ZEXTRI_EMAIL_CONFIG.senderEmail}>`,
      subject,
      bodyHtml,
      bodyText,
      warnings: this.getDraftWarnings(lead),
      isTest,
      templateName: this.selectedEmailTemplateName() || EmailStrings.preview.customDraftName,
      templateCategory: this.selectedOrDefaultTemplate()?.category ?? '',
      templateVersion: this.selectedEmailTemplateVersion() || EmailStrings.preview.draftVersion
    };
  }

  protected formatContactedAt(value: string): string {
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return value;
    }

    return new Intl.DateTimeFormat(undefined, {
      dateStyle: 'medium',
      timeStyle: 'short'
    }).format(date);
  }

  private defaultEmailCopy(): string {
    return DEFAULT_EMAIL_TEMPLATE.html;
  }

  private resolveVariables(value: string, lead: Lead, forHtml = true): string {
    const company = this.companyNameOrFallback(lead);
    const variables: Record<string, string> = {
      brandName: EmailStrings.brand.name,
      logoAlt: EmailStrings.brand.logoAlt,
      logoUrl: ZEXTRI_EMAIL_CONFIG.logoUrl,
      firstName: this.firstName(lead),
      fullName: lead.displayName.trim(),
      company,
      senderName: ZEXTRI_EMAIL_CONFIG.senderName,
      senderTitle: ZEXTRI_EMAIL_CONFIG.senderTitle,
      websiteUrl: ZEXTRI_EMAIL_CONFIG.websiteUrl,
      demoUrl: ZEXTRI_EMAIL_CONFIG.demoUrl,
      quickIntroductionDemoUrl: ZEXTRI_EMAIL_CONFIG.demoUrl,
      relationshipDemoUrl: ZEXTRI_EMAIL_CONFIG.relationshipDemoUrl,
      followUpDemoUrl: ZEXTRI_EMAIL_CONFIG.followUpDemoUrl,
      chromeUrl: ZEXTRI_EMAIL_CONFIG.chromeUrl,
      edgeUrl: ZEXTRI_EMAIL_CONFIG.edgeUrl,
      chromeIconUrl: ZEXTRI_EMAIL_CONFIG.chromeIconUrl,
      edgeIconUrl: ZEXTRI_EMAIL_CONFIG.edgeIconUrl,
      unsubscribeUrl: ZEXTRI_EMAIL_CONFIG.unsubscribeUrl,
      businessInfo: ZEXTRI_EMAIL_CONFIG.businessInfo,
      emailGreeting: EmailStrings.templates.shared.greeting,
      ctaWatchDemo: EmailStrings.templates.shared.ctaWatchDemo,
      ctaChrome: EmailStrings.templates.shared.ctaChrome,
      ctaEdge: EmailStrings.templates.shared.ctaEdge,
      storeAvailability: EmailStrings.templates.shared.storeAvailability,
      signatureClosing: EmailStrings.templates.shared.signatureClosing,
      signatureWebsiteLabel: EmailStrings.templates.shared.signatureWebsiteLabel,
      unsubscribeLabel: EmailStrings.templates.shared.unsubscribeLabel,
      unsubscribeSuffix: EmailStrings.templates.shared.unsubscribeSuffix,
      quickIntroductionOpening: this.resolveEmailStringTemplate(EmailStrings.templates.quickIntroduction.opening, { company }),
      quickIntroductionBody: EmailStrings.templates.quickIntroduction.body,
      quickIntroductionQuestion: EmailStrings.templates.quickIntroduction.question,
      relationshipValueOpening: this.resolveEmailStringTemplate(EmailStrings.templates.relationshipValue.opening, { company }),
      relationshipSignalEyebrow: EmailStrings.templates.relationshipValue.signalEyebrow,
      relationshipSignalHeading: EmailStrings.templates.relationshipValue.signalHeading,
      relationshipSignalBody: EmailStrings.templates.relationshipValue.signalBody,
      relationshipValueQuestion: EmailStrings.templates.relationshipValue.question,
      demoFollowUpOpening: this.resolveEmailStringTemplate(EmailStrings.templates.demoFollowUp.opening, { company }),
      demoFollowUpBody: EmailStrings.templates.demoFollowUp.body
    };

    return value.replace(/\{\{\s*([a-zA-Z0-9]+)\s*\}\}/g, (_, key: string) => {
      const replacement = variables[key] ?? '';
      return forHtml ? this.escapeHtml(replacement) : replacement;
    });
  }

  private resolveEmailStringTemplate(value: string, variables: Record<string, string>): string {
    return value.replace(/\{\{\s*([a-zA-Z0-9]+)\s*\}\}/g, (_, key: string) => variables[key] ?? '');
  }

  private getDraftWarnings(lead: Lead | null): string[] {
    if (!lead) {
      return [EmailStrings.ui.warnings.noLead];
    }

    const warnings: string[] = [];
    const firstName = this.firstName(lead);
    const bodyText = this.normalizeText(this.htmlToText(this.finalEmailBodyHtml(lead)));
    const subjectText = this.normalizeText(this.resolveVariables(this.emailSubject(), lead, false));
    const missingRequiredVariables = this.getMissingRequiredVariables(lead);
    const unresolvedVariables = this.findUnresolvedVariables(`${subjectText} ${bodyText}`);

    if (!firstName) warnings.push(EmailStrings.ui.warnings.missingGreeting);
    if (!lead.publicEmail) warnings.push(EmailStrings.ui.warnings.missingRecipient);
    if (lead.contactStatus === 'DoNotContact') warnings.push(EmailStrings.ui.warnings.doNotContact);
    for (const variable of missingRequiredVariables) {
      warnings.push(`${EmailStrings.ui.warnings.missingRequiredVariablePrefix} {{${variable}}}.`);
    }
    if (this.emailTo().trim() && lead.publicEmail && this.emailTo().trim().toLowerCase() !== lead.publicEmail.trim().toLowerCase()) {
      warnings.push(EmailStrings.ui.warnings.recipientMismatch);
    }
    if (unresolvedVariables.length > 0) {
      warnings.push(`${EmailStrings.ui.warnings.unresolvedVariablesPrefix} ${unresolvedVariables.map(variable => `{{${variable}}}`).join(', ')}.`);
    }

    return warnings;
  }

  private getMissingRequiredVariables(lead: Lead): string[] {
    const template = this.emailTemplates().find(item => item.id === this.selectedEmailTemplateId());
    if (!template) {
      return [];
    }

    const values: Record<string, string> = {
      firstName: this.firstName(lead),
      fullName: lead.displayName.trim(),
      company: this.companyNameOrFallback(lead),
      senderName: ZEXTRI_EMAIL_CONFIG.senderName,
      senderTitle: ZEXTRI_EMAIL_CONFIG.senderTitle,
      websiteUrl: ZEXTRI_EMAIL_CONFIG.websiteUrl,
      demoUrl: ZEXTRI_EMAIL_CONFIG.demoUrl,
      quickIntroductionDemoUrl: ZEXTRI_EMAIL_CONFIG.demoUrl,
      relationshipDemoUrl: ZEXTRI_EMAIL_CONFIG.relationshipDemoUrl,
      followUpDemoUrl: ZEXTRI_EMAIL_CONFIG.followUpDemoUrl,
      chromeUrl: ZEXTRI_EMAIL_CONFIG.chromeUrl,
      edgeUrl: ZEXTRI_EMAIL_CONFIG.edgeUrl,
      chromeIconUrl: ZEXTRI_EMAIL_CONFIG.chromeIconUrl,
      edgeIconUrl: ZEXTRI_EMAIL_CONFIG.edgeIconUrl,
      unsubscribeUrl: ZEXTRI_EMAIL_CONFIG.unsubscribeUrl,
      businessInfo: ZEXTRI_EMAIL_CONFIG.businessInfo,
      brandName: EmailStrings.brand.name,
      logoAlt: EmailStrings.brand.logoAlt,
      logoUrl: ZEXTRI_EMAIL_CONFIG.logoUrl,
      emailGreeting: EmailStrings.templates.shared.greeting,
      ctaWatchDemo: EmailStrings.templates.shared.ctaWatchDemo,
      ctaChrome: EmailStrings.templates.shared.ctaChrome,
      ctaEdge: EmailStrings.templates.shared.ctaEdge,
      storeAvailability: EmailStrings.templates.shared.storeAvailability,
      signatureClosing: EmailStrings.templates.shared.signatureClosing,
      signatureWebsiteLabel: EmailStrings.templates.shared.signatureWebsiteLabel,
      unsubscribeLabel: EmailStrings.templates.shared.unsubscribeLabel,
      unsubscribeSuffix: EmailStrings.templates.shared.unsubscribeSuffix,
      quickIntroductionOpening: EmailStrings.templates.quickIntroduction.opening,
      quickIntroductionBody: EmailStrings.templates.quickIntroduction.body,
      quickIntroductionQuestion: EmailStrings.templates.quickIntroduction.question,
      relationshipValueOpening: EmailStrings.templates.relationshipValue.opening,
      relationshipSignalEyebrow: EmailStrings.templates.relationshipValue.signalEyebrow,
      relationshipSignalHeading: EmailStrings.templates.relationshipValue.signalHeading,
      relationshipSignalBody: EmailStrings.templates.relationshipValue.signalBody,
      relationshipValueQuestion: EmailStrings.templates.relationshipValue.question,
      demoFollowUpOpening: EmailStrings.templates.demoFollowUp.opening,
      demoFollowUpBody: EmailStrings.templates.demoFollowUp.body
    };

    return template.requiredVariables.filter(variable => !values[variable]?.trim());
  }

  private findUnresolvedVariables(value: string): string[] {
    return Array.from(value.matchAll(/\{\{\s*([a-zA-Z0-9]+)\s*\}\}/g))
      .map(match => match[1])
      .filter((variable, index, variables) => variables.indexOf(variable) === index);
  }

  private firstName(lead: Lead): string {
    return lead.displayName.trim().split(/\s+/)[0] ?? '';
  }

  private companyNameOrFallback(lead: Lead): string {
    return lead.company?.name?.trim() || EmailStrings.templates.shared.companyFallback;
  }

  private isValidEmail(value: string): boolean {
    return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value.trim());
  }

  private markDraftDirty(): void {
    this.draftDirty.set(true);
    this.emailPreview.set(null);
  }

  private textToHtml(value: string): string {
    return value
      .split(/\r?\n/)
      .map(line => line.trim())
      .filter(line => line.length > 0)
      .map(line => `<p>${this.escapeHtml(line)}</p>`)
      .join('');
  }

  private htmlToText(value: string): string {
    const container = document.createElement('div');
    container.innerHTML = value;
    return (container.innerText || container.textContent || '').trim();
  }

  private escapeHtml(value: string): string {
    const container = document.createElement('div');
    container.textContent = value;
    return container.innerHTML;
  }

  private showToast(kind: ToastKind, title: string, body: string): void {
    this.toast.set({ kind, title, body });
    if (this.toastTimer) {
      window.clearTimeout(this.toastTimer);
    }
    this.toastTimer = window.setTimeout(() => this.toast.set(null), 6500);
  }

  private getErrorMessage(error: HttpErrorResponse, fallback: string): string {
    if (typeof error.error === 'string' && error.error.trim()) {
      return error.error;
    }

    if (error.error?.message) {
      return error.error.message;
    }

    return fallback;
  }

  private loadEmailSettings(): void {
    const saved = localStorage.getItem('signalminer.emailSettings');
    if (!saved) {
      this.setEmailSignatureHtml(this.zextriSignatureHtml());
      return;
    }

    try {
      const settings = JSON.parse(saved) as {
        replyTo?: string;
        cc?: string;
        bcc?: string;
        signatureHtml?: string;
        signature?: string;
        includeSignature?: boolean;
      };
      this.emailReplyTo.set(ZEXTRI_EMAIL_CONFIG.senderEmail);
      this.emailCc.set(settings.cc ?? '');
      this.emailBcc.set(settings.bcc ?? '');
      this.setEmailSignatureHtml(this.normalizeSavedSignature(settings.signatureHtml, settings.signature));
      this.includeSignature.set(settings.includeSignature ?? true);
    } catch {
      this.message.set(EmailStrings.ui.messages.savedSettingsLoadFailed);
      this.setEmailSignatureHtml(this.zextriSignatureHtml());
    }
  }

  private finalEmailBodyHtml(lead = this.selectedLead(), bodyHtml = this.emailBodyHtml()): string {
    const sanitizedBody = this.sanitizeEditableHtml(bodyHtml);
    const resolvedBody = lead
      ? this.resolveVariables(sanitizedBody, lead)
      : sanitizedBody;

    if (this.selectedEmailTemplateId()) {
      return this.ensureStyledTemplateEmail(resolvedBody);
    }

    const signedBody = this.includeSignature()
      ? this.withDefaultSignature(resolvedBody)
      : this.removeSignature(resolvedBody);
    return this.withComplianceFooter(signedBody);
  }

  private bulkTemplateBodyHtml(previewLead?: Lead): string | undefined {
    const templateHtml = this.activeEmailTemplateHtml().trim();
    if (!previewLead || !templateHtml) {
      return undefined;
    }

    return this.composeEmailTemplateHtml(templateHtml);
  }

  private pruneSelectedLeadIds(leads: Lead[]): void {
    const visibleIds = new Set(leads.map(lead => lead.id));
    this.selectedLeadIds.update(ids => new Set(Array.from(ids).filter(id => visibleIds.has(id))));
  }

  private ensureStyledTemplateEmail(bodyHtml: string): string {
    const repaired = this.repairEmailClientStyles(bodyHtml);
    if (repaired.includes('data-zextri-email-layout="true"') || repaired.includes('max-width:640px')) {
      return repaired;
    }

    const layout = this.sharedEmailTemplateParts?.layout ?? FALLBACK_EMAIL_LAYOUT_HTML;
    return layout.replace('{{emailContent}}', repaired);
  }

  private repairEmailClientStyles(bodyHtml: string): string {
    const container = document.createElement('div');
    container.innerHTML = bodyHtml;
    this.styleEmailLink(container, EmailStrings.templates.shared.ctaWatchDemo, {
      table: 'border-collapse:collapse;margin:0 0 10px;',
      td: 'border-radius:10px;padding:12px 18px;',
      bg: '#07070a',
      anchor: 'font-family:Inter,Arial,Helvetica,sans-serif;font-size:14px;line-height:18px;font-weight:800;color:#ffffff;text-decoration:none;',
      span: 'color:#ffffff;text-decoration:none;'
    });
    this.styleEmailLink(container, EmailStrings.templates.shared.ctaChrome, {
      table: 'border-collapse:collapse;margin:0 0 10px;',
      td: 'border:1px solid #d9dee7;border-radius:9px;padding:10px 18px;',
      bg: '#ffffff',
      anchor: 'font-family:Inter,Arial,Helvetica,sans-serif;font-size:14px;line-height:18px;font-weight:800;color:#202124;text-decoration:none;',
      span: 'color:#202124;text-decoration:none;',
      iconUrl: ZEXTRI_EMAIL_CONFIG.chromeIconUrl
    });
    this.styleEmailLink(container, EmailStrings.templates.shared.ctaEdge, {
      table: 'border-collapse:collapse;margin:0 0 10px;',
      td: 'border:1px solid #1473c7;border-radius:9px;padding:10px 18px;',
      bg: '#1473c7',
      anchor: 'font-family:Inter,Arial,Helvetica,sans-serif;font-size:14px;line-height:18px;font-weight:800;color:#ffffff;text-decoration:none;',
      span: 'color:#ffffff;text-decoration:none;',
      iconUrl: ZEXTRI_EMAIL_CONFIG.edgeIconUrl,
      aliases: ['Get for Edge']
    });
    this.styleEmailLink(container, EmailStrings.templates.shared.signatureWebsiteLabel, {
      anchor: 'color:#2563eb;text-decoration:none;font-weight:700;',
      span: 'color:#2563eb;text-decoration:none;',
      block: 'margin:6px 0 0;font-size:13px;line-height:19px;'
    });
    this.styleEmailLink(container, EmailStrings.templates.shared.unsubscribeLabel, {
      anchor: 'color:#2563eb;text-decoration:none;',
      span: 'color:#2563eb;text-decoration:none;',
      block: 'margin-top:26px;padding-top:12px;border-top:1px solid #e5e7eb;font-family:Inter,Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;color:#6b7280;'
    });
    this.styleTextBlock(container, EmailStrings.templates.shared.storeAvailability, 'margin:10px 0 28px;font-size:12px;line-height:18px;color:#64748b;');
    this.styleTextBlock(container, EmailStrings.templates.shared.signatureClosing, 'margin:0 0 2px;font-size:15px;line-height:21px;font-weight:700;color:#111827;');
    this.styleTextBlock(container, ZEXTRI_EMAIL_CONFIG.senderTitle, 'margin:6px 0 0;font-size:13px;line-height:19px;color:#4b5563;');
    return container.innerHTML.trim();
  }

  private styleEmailLink(
    container: HTMLElement,
    label: string,
    styles: {
      table?: string;
      td?: string;
      bg?: string;
      anchor: string;
      span: string;
      block?: string;
      iconUrl?: string;
      aliases?: string[];
    }
  ): void {
    const labels = [label, ...(styles.aliases ?? [])];
    const anchor = Array.from(container.querySelectorAll('a'))
      .find(item => labels.includes((item.textContent ?? '').trim()));
    if (!anchor) {
      return;
    }

    anchor.setAttribute('style', styles.anchor);
    const icon = styles.iconUrl
      ? `<img src="${this.escapeHtml(styles.iconUrl)}" width="18" height="18" alt="" style="display:inline-block;width:18px;height:18px;vertical-align:-4px;margin-right:10px;border:0;">`
      : '';
    anchor.innerHTML = `${icon}<span style="${styles.span}">${this.escapeHtml(label)}</span>`;

    const cell = anchor.closest('td');
    if (cell && styles.td) {
      cell.setAttribute('style', styles.td);
      if (styles.bg) {
        cell.setAttribute('bgcolor', styles.bg);
      }
    }

    const table = anchor.closest('table');
    if (table && styles.table) {
      table.setAttribute('style', styles.table);
    }

    const block = anchor.parentElement;
    if (block && block !== container && styles.block) {
      block.setAttribute('style', styles.block);
    }
  }

  private styleTextBlock(container: HTMLElement, text: string, style: string): void {
    const element = this.findSmallestElementWithExactText(container, text);
    if (!element || element === container) {
      return;
    }

    element.setAttribute('style', style);
  }

  private findSmallestElementWithExactText(container: HTMLElement, text: string): HTMLElement | null {
    const normalizedText = this.normalizeText(text);
    let best: HTMLElement | null = null;
    container.querySelectorAll<HTMLElement>('*').forEach(element => {
      if (this.normalizeText(element.textContent ?? '') !== normalizedText) {
        return;
      }

      if (!best || element.innerHTML.length < best.innerHTML.length) {
        best = element;
      }
    });

    return best;
  }

  private sanitizeEditableHtml(value: string): string {
    const container = document.createElement('div');
    container.innerHTML = value;
    container.querySelectorAll('script, style, iframe, object, embed').forEach(element => element.remove());
    container.querySelectorAll('*').forEach(element => {
      for (const attribute of Array.from(element.attributes)) {
        const name = attribute.name.toLowerCase();
        const attrValue = attribute.value.trim().toLowerCase();
        if (name.startsWith('on') || attrValue.startsWith('javascript:')) {
          element.removeAttribute(attribute.name);
        }
      }
    });

    return container.innerHTML.trim();
  }

  private withComplianceFooter(bodyHtml: string): string {
    const footer = [
      '<div data-signalminer-compliance="true" style="margin-top:18px;padding-top:10px;border-top:1px solid #e5e7eb;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;color:#6b7280;">',
      `<div><a href="${ZEXTRI_EMAIL_CONFIG.unsubscribeUrl}" style="color:#0f766e;text-decoration:none;">${EmailStrings.templates.shared.unsubscribeLabel}</a>${EmailStrings.templates.shared.unsubscribeSuffix}</div>`,
      '</div>'
    ].join('');
    const withoutFooter = bodyHtml.replace(/<div data-signalminer-compliance="true"[\s\S]*?<\/div>\s*<\/div>/gi, '').trim();
    return `${withoutFooter}${footer}`;
  }

  private signatureHtml(): string {
    return this.emailSignatureHtml().trim();
  }

  private appendSignature(bodyHtml: string, signatureHtml: string): string {
    if (this.hasSignature(bodyHtml)) {
      return bodyHtml;
    }

    return `${bodyHtml}<br>${signatureHtml}`;
  }

  private withDefaultSignature(bodyHtml: string): string {
    const signatureHtml = this.signatureHtml();
    return this.includeSignature() && signatureHtml
      ? this.appendSignature(this.removeSignature(bodyHtml), signatureHtml)
      : this.removeSignature(bodyHtml);
  }

  private removeSignature(bodyHtml: string): string {
    const container = document.createElement('div');
    container.innerHTML = bodyHtml;
    container.querySelectorAll('[data-signalminer-signature="true"], .email-signature').forEach(signature => {
      const previous = signature.previousSibling;
      if (previous?.nodeName === 'BR') {
        previous.remove();
      }
      signature.remove();
    });

    let signatureBlock = this.findSignatureBlock(container);
    while (signatureBlock) {
      this.removeNodeAndFollowingSiblings(signatureBlock);
      signatureBlock = this.findSignatureBlock(container);
    }

    return container.innerHTML.replace(/(<br\s*\/?>|\s|&nbsp;)+$/gi, '').trim();
  }

  private hasSignature(bodyHtml: string): boolean {
    if (bodyHtml.includes('data-signalminer-signature="true"') || bodyHtml.includes('class="email-signature"')) {
      return true;
    }

    const normalizedText = this.normalizeText(this.htmlToText(bodyHtml));
    return SIGNATURE_TEXT_MARKERS.some(marker => normalizedText.includes(marker));
  }

  private findSignatureBlock(container: HTMLElement): Node | null {
    const walker = document.createTreeWalker(container, NodeFilter.SHOW_TEXT);
    let current = walker.nextNode();
    while (current) {
      const text = this.normalizeText(current.textContent ?? '');
      if (SIGNATURE_TEXT_MARKERS.some(marker => text.includes(marker))) {
        return this.findTopLevelSignatureNode(container, current);
      }

      current = walker.nextNode();
    }

    return null;
  }

  private findTopLevelSignatureNode(container: HTMLElement, node: Node): Node {
    let current = node;
    while (current.parentNode && current.parentNode !== container) {
      current = current.parentNode;
    }

    return current;
  }

  private removeNodeAndFollowingSiblings(node: Node): void {
    const parent = node.parentNode;
    if (!parent) {
      return;
    }

    let current: ChildNode | null = node as ChildNode;
    while (current) {
      const next: ChildNode | null = current.nextSibling;
      current.remove();
      current = next;
    }
  }

  private normalizeText(value: string): string {
    return value
      .toLowerCase()
      .replace(/\s+/g, ' ')
      .trim();
  }

  private normalizeSavedSignature(signatureHtml?: string, legacySignature?: string): string {
    if (signatureHtml?.trim() && !this.isLegacyZextriSignature(signatureHtml)) {
      return signatureHtml.trim();
    }

    if (!legacySignature?.trim() || this.isLegacyZextriSignature(this.textToHtml(legacySignature))) {
      return this.zextriSignatureHtml();
    }

    return this.customSignatureHtml(legacySignature);
  }

  private isLegacyZextriSignature(signatureHtml: string): boolean {
    const normalizedText = this.normalizeText(this.htmlToText(signatureHtml));
    return LEGACY_ZEXTRI_SIGNATURE_MARKERS.some(marker => normalizedText.includes(marker));
  }

  private zextriSignatureHtml(): string {
    return FALLBACK_ZEXTRI_SIGNATURE_HTML;
  }

  private customSignatureHtml(signature: string): string {
    return `${CUSTOM_SIGNATURE_WRAPPER.before}${this.textToHtml(signature)}${CUSTOM_SIGNATURE_WRAPPER.after}`;
  }

  protected updateStatus(status: ContactStatus): void {
    const lead = this.selectedLead();
    if (!lead) return;

    this.http.patch<Lead>(`${this.apiBase}/leads/${lead.id}/outreach-status`, {
      status,
      note: this.outreachNote() || `Manual status changed to ${status}`
    }).subscribe(updated => {
      this.selectedLead.set(updated);
      this.message.set('Manual outreach status updated.');
      this.search();
    });
  }

  private uploadImport(commit: boolean): void {
    const file = this.importFile();
    if (!file) {
      this.message.set('Choose a CSV or Excel file first.');
      return;
    }

    this.submitImport(file, commit, 'file');
  }

  private submitImport(file: File, commit: boolean, source: 'file' | 'manual'): void {
    this.loading.set(true);
    const form = new FormData();
    form.append('file', file);
    form.append('commit', String(commit));

    this.http.post<LeadImportResult>(`${this.apiBase}/leads/import`, form).subscribe({
      next: result => {
        this.importResult.set(result);
        const message = this.importMessage(result, commit, source);
        this.message.set(message);
        if (source === 'manual' && commit) {
          this.showToast(
            result.importedRows > 0 ? 'success' : 'error',
            result.importedRows > 0 ? EmailStrings.ui.toasts.contactAddedTitle : EmailStrings.ui.toasts.contactNotAddedTitle,
            this.manualContactToastMessage(result, message));
        }
        this.loading.set(false);
        if (commit) {
          this.sourceFilter.set('Imported');
          if (source === 'manual' && result.importedRows > 0) {
            this.resetManualContact();
          }
          this.search();
        }
      },
      error: (error: HttpErrorResponse) => {
        this.message.set(error.error?.message || 'Import failed. Check the file format and required columns.');
        this.loading.set(false);
      }
    });
  }

  private importMessage(result: LeadImportResult, commit: boolean, source: 'file' | 'manual'): string {
    if (!commit) {
      return `Previewed ${result.validRows} valid rows from ${result.totalRows}.`;
    }

    if (source === 'manual') {
      return result.importedRows > 0
        ? 'Contact added manually.'
        : `Contact was not added; skipped ${result.skippedRows}.`;
    }

    return `Imported ${result.importedRows} leads; skipped ${result.skippedRows}.`;
  }

  private manualContactToastMessage(result: LeadImportResult, fallback: string): string {
    const duplicateReason = result.previewRows
      .map(row => row.duplicateReason?.trim())
      .find(Boolean);
    if (duplicateReason) {
      return duplicateReason;
    }

    const issue = result.issues[0];
    if (issue) {
      return `${issue.field}: ${issue.message}`;
    }

    return fallback;
  }

  private resetManualContact(): void {
    this.manualContact.set({
      name: '',
      email: '',
      title: '',
      company: '',
      companyDomain: '',
      linkedInUrl: '',
      note: ''
    });
  }

  private buildManualContactFile(contact: ManualContactForm): File {
    const headers = [
      'Display Name',
      'Professional Email',
      'Role / Title',
      'Company',
      'Company Domain',
      'LinkedIn',
      'Notes',
      'Outreach Status',
      'Original Fit Score'
    ];
    const note = ['Manually added from dashboard.', contact.note.trim()]
      .filter(Boolean)
      .join(' ');
    const row = [
      contact.name.trim(),
      contact.email.trim(),
      contact.title.trim(),
      contact.company.trim(),
      contact.companyDomain.trim(),
      contact.linkedInUrl.trim(),
      note,
      'NotContacted',
      '50'
    ];
    const csv = `${headers.map(this.csvCell).join(',')}\r\n${row.map(this.csvCell).join(',')}\r\n`;
    return new File([csv], 'manual-contact.csv', { type: 'text/csv' });
  }

  private csvCell(value: string): string {
    return `"${value.replace(/"/g, '""')}"`;
  }
}
