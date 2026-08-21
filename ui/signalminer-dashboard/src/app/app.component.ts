import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';

type ContactStatus = 'NotContacted' | 'ReadyForManualOutreach' | 'Contacted' | 'Replied' | 'NotInterested' | 'DoNotContact';
type LeadStatus = 'New' | 'Enriched' | 'NeedsManualReview' | 'Qualified' | 'Disqualified' | 'Archived';
type DiscoverySource = 'GitHub' | 'X';
type SourceKind = 'GitHub' | 'Website' | 'X' | 'LinkedInProfileUrlOnly' | 'Manual';
type SourceFilter = '' | 'Imported' | SourceKind;
type DetailTab = 'overview' | 'outreach' | 'email';
type ToastKind = 'success' | 'error' | 'info';

const ZEXTRI_LOGO_URL = 'https://zextri.com/icons/zextri-192.png';
const SENDER_EMAIL = 'hello@zextri.com';
const SENDER_NAME = 'Bharat from Zextri';
const BUSINESS_INFO = 'Zextri, hello@zextri.com, https://zextri.com';
const UNSUBSCRIBE_URL = 'https://zextri.com/unsubscribe';
const WEBSITE_URL = 'https://zextri.com';
const DEMO_URL = 'https://zextri.com/#/';
const EMAIL_TEMPLATE_MANIFEST_URL = 'assets/email-templates/manifest.json';
const ZEXTRI_EMAIL_SIGNATURE_TEXT = [
  'Warm regards,',
  'Bharat Bhushan',
  'Founder, Zextri',
  'Website: https://zextri.com'
].join('\n');

const LEGACY_EMAIL_SIGNATURE = [
  'Best regards,',
  'Zextri Team',
  'https://zextri.com'
].join('\n');

const SIGNATURE_TEXT_MARKERS = [
  'best regards',
  'warm regards',
  'bharat bhushan',
  'zextri growth team',
  'zextri team',
  'website: https://zextri.com',
  'zextri.com'
];

interface EmailPreview {
  recipient: string;
  sender: string;
  subject: string;
  bodyHtml: string;
  bodyText: string;
  warnings: string[];
  isTest: boolean;
  templateName: string;
  templateVersion: string;
}

interface EmailTemplateMetadata {
  id: string;
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
  rowNumber: number;
  field: string;
  message: string;
}

interface LeadImportPreviewRow {
  rowNumber: number;
  displayName: string;
  company?: string;
  publicEmail?: string;
  fitScore: number;
  isDuplicate: boolean;
  duplicateReason?: string;
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
  private readonly apiBase = 'http://localhost:5126/api';
  private toastTimer: number | undefined;
  private emailEditorVersion = 0;
  private sharedEmailTemplateParts: SharedEmailTemplateParts | null = null;
  private templateHtmlCache = new Map<string, string>();

  protected readonly leads = signal<Lead[]>([]);
  protected readonly selectedLead = signal<Lead | null>(null);
  protected readonly total = signal(0);
  protected readonly query = signal('');
  protected readonly minFitScore = signal<number | null>(50);
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
  protected readonly emailAttachments = signal<File[]>([]);
  protected readonly emailReplyTo = signal('');
  protected readonly emailCc = signal('');
  protected readonly emailBcc = signal('');
  protected readonly emailSignatureHtml = signal('');
  protected readonly includeSignature = signal(true);
  protected readonly emailSending = signal(false);
  protected readonly draftDirty = signal(false);
  protected readonly activeEmailLeadId = signal<string | null>(null);
  protected readonly emailPreview = signal<EmailPreview | null>(null);
  protected readonly emailTemplates = signal<EmailTemplateMetadata[]>([]);
  protected readonly selectedEmailTemplateId = signal('');
  protected readonly selectedEmailTemplateVersion = signal('');
  protected readonly selectedEmailTemplateName = signal('');
  protected readonly importFile = signal<File | null>(null);
  protected readonly importResult = signal<LeadImportResult | null>(null);
  protected readonly detailTab = signal<DetailTab>('overview');
  protected readonly detailExpanded = signal(true);
  protected readonly toast = signal<ToastMessage | null>(null);

  protected readonly totalPages = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize())));

  protected readonly pageStart = computed(() => {
    if (this.total() === 0) return 0;
    return (this.page() - 1) * this.pageSize() + 1;
  });

  protected readonly pageEnd = computed(() => Math.min(this.total(), this.page() * this.pageSize()));

  constructor() {
    this.loadEmailSettings();
    this.loadEmailTemplates();
    this.search();
  }

  protected search(resetPage = true): void {
    if (resetPage) {
      this.page.set(1);
    }

    this.loading.set(true);
    let params = new HttpParams()
      .set('page', this.page())
      .set('pageSize', this.pageSize());

    if (this.query()) params = params.set('query', this.query());
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
      error: () => {
        this.message.set('API unavailable. Start SignalMiner.Api on http://localhost:5000.');
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
        this.search();
      },
      error: (error: HttpErrorResponse) => {
        this.message.set(error.error?.message || 'Discovery failed. Check API, PostgreSQL, and source rate limits.');
        this.loading.set(false);
      }
    });
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

  protected primarySource(lead: Lead): { label: string; url?: string } {
    if (lead.gitHubUrl) return { label: 'GitHub', url: lead.gitHubUrl };
    if (lead.xUrl) return { label: 'X', url: lead.xUrl };
    if (lead.linkedInUrl) return { label: 'LinkedIn', url: lead.linkedInUrl };
    if (lead.websiteUrl) return { label: 'Website', url: lead.websiteUrl };
    return { label: 'None' };
  }

  protected clearFilters(): void {
    this.query.set('');
    this.minFitScore.set(null);
    this.contactStatus.set('');
    this.sourceFilter.set('');
    this.search();
  }

  protected toggleDetailExpanded(): void {
    this.detailExpanded.update(value => !value);
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
      const shouldSwitch = window.confirm('You have an unsaved email draft. Switch contacts and replace the draft?');
      if (!shouldSwitch) {
        return;
      }
    }

    this.prepareLeadEmail(lead);
  }

  private prepareLeadEmail(lead: Lead): void {
    this.selectedLead.set(lead);
    this.activeEmailLeadId.set(lead.id);
    this.detailTab.set('overview');
    this.outreachNote.set('');
    this.emailTo.set(lead.publicEmail ?? '');
    this.emailReplyTo.set(SENDER_EMAIL);
    this.emailAttachments.set([]);
    this.emailPreview.set(null);
    this.draftDirty.set(false);
    this.emailEditorVersion++;
    const templateId = this.selectedEmailTemplateId() || this.emailTemplates()[0]?.id;
    if (templateId) {
      this.applyEmailTemplate(templateId, false);
    } else {
      this.emailSubject.set('A quick idea for {{company}}');
      this.emailBodyHtml.set(this.composeEmailTemplateHtml(this.defaultEmailCopy()));
      this.selectedEmailTemplateName.set('Quick Introduction');
      this.selectedEmailTemplateVersion.set('1.0.0');
    }
  }

  protected selectEmailTemplate(templateId: string): void {
    this.applyEmailTemplate(templateId, true);
  }

  protected resetToTemplate(): void {
    const template = this.selectedOrDefaultTemplate();
    if (!template) {
      this.showToast('error', 'Templates loading', 'Email templates are still loading. Try again in a moment.');
      return;
    }

    this.applyEmailTemplate(template.id, false);
  }

  protected updateEmailBody(event: Event): void {
    this.emailBodyHtml.set((event.target as HTMLElement).innerHTML);
    this.markDraftDirty();
  }

  protected updateEmailSignature(event: Event): void {
    this.emailSignatureHtml.set((event.target as HTMLElement).innerHTML);
    if (this.includeSignature()) {
      this.emailBodyHtml.set(this.withDefaultSignature(this.emailBodyHtml()));
    }
    this.markDraftDirty();
  }

  protected setIncludeSignature(value: boolean): void {
    this.includeSignature.set(value);
    this.emailBodyHtml.set(value
      ? this.withDefaultSignature(this.emailBodyHtml())
      : this.removeSignature(this.emailBodyHtml()));
    this.markDraftDirty();
  }

  protected formatEmail(command: string, value?: string): void {
    document.execCommand(command, false, value);
  }

  protected addEmailLink(): void {
    const url = window.prompt('Link URL');
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
    this.message.set('Email settings saved for this browser.');
  }

  protected insertSignature(): void {
    const signatureHtml = this.signatureHtml();
    if (!signatureHtml) {
      this.message.set('Add a signature in email settings first.');
      return;
    }

    this.emailBodyHtml.set(this.withDefaultSignature(this.emailBodyHtml()));
    this.markDraftDirty();
  }

  protected useZextriSignature(): void {
    this.emailSignatureHtml.set(this.zextriSignatureHtml());
    this.includeSignature.set(true);
    this.emailBodyHtml.set(this.withDefaultSignature(this.removeSignature(this.emailBodyHtml())));
    this.message.set('Market-ready Zextri signature applied.');
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
      ? `Hi {{firstName}},\n\nI came across your work at {{company}} and thought Zextri could be useful for your growth workflow.`
      : `Hi ${firstName || 'there'},\n\nI thought Zextri could be useful for your growth workflow.`;
    const paragraphs = this.htmlToText(this.removeSignature(this.emailBodyHtml()))
      .split(/\n{2,}/)
      .map(part => part.trim())
      .filter(Boolean);
    const rest = paragraphs.slice(2).join('\n\n') ||
      'Zextri helps teams identify relationships that need attention and write timely, context-aware follow-ups across LinkedIn and X.\n\nWould you be open to a quick look? I\'d be happy to send a short demo.';

    this.emailBodyHtml.set(this.withDefaultSignature(this.textToHtml(`${opening}\n\n${rest}`)));
    this.markDraftDirty();
    this.previewEmail();
  }

  protected sendTestEmail(): void {
    const preview = this.buildEmailPreview(true);
    if (!preview) {
      return;
    }

    this.emailPreview.set(preview);
    const confirmed = window.confirm(
      `Send test email?\n\nLead: ${this.selectedLead()?.displayName ?? 'Selected lead'}\nTo: ${preview.recipient}\nFrom: ${preview.sender}\nTemplate: ${preview.templateName} v${preview.templateVersion}\nSubject: ${preview.subject}\n\n${preview.bodyText}`
    );
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
    const confirmed = window.confirm(
      `Send email?\n\nLead: ${this.leadSummary(this.selectedLead())}\nTo: ${preview.recipient}\nFrom: ${preview.sender}\nTemplate: ${preview.templateName} v${preview.templateVersion}\nSubject: ${preview.subject}\n\n${preview.bodyText}`
    );
    if (!confirmed) {
      return;
    }

    this.submitEmail(preview);
  }

  private submitEmail(preview: EmailPreview): void {
    const lead = this.selectedLead();
    if (!lead) return;

    if (this.emailSending()) return;

    this.loading.set(true);
    this.emailSending.set(true);
    const form = new FormData();
    form.append('toEmail', preview.recipient);
    form.append('subject', preview.subject);
    form.append('body', preview.bodyText);
    form.append('bodyHtml', preview.bodyHtml);
    form.append('replyTo', SENDER_EMAIL);
    if (this.emailCc().trim()) form.append('cc', this.emailCc().trim());
    if (this.emailBcc().trim()) form.append('bcc', this.emailBcc().trim());
    form.append('isTest', String(preview.isTest));
    form.append('templateId', this.selectedEmailTemplateId());
    form.append('templateVersion', preview.templateVersion);
    for (const file of this.emailAttachments()) {
      form.append('attachments', file, file.name);
    }

    this.http.post<Lead>(`${this.apiBase}/leads/${lead.id}/email`, form).subscribe({
      next: updated => {
        this.selectedLead.set(updated);
        const successMessage = preview.isTest
          ? `Test email sent to ${preview.recipient}.`
          : `Email submitted to ${preview.recipient}.`;
        this.message.set(successMessage);
        this.showToast('success', 'Email sent', successMessage);
        this.loading.set(false);
        this.emailSending.set(false);
        if (!preview.isTest) {
          this.draftDirty.set(false);
        }
        this.search(false);
      },
      error: (error: HttpErrorResponse) => {
        const errorMessage = this.getErrorMessage(error, 'Email could not be sent. Check SMTP settings and try again.');
        this.message.set(errorMessage);
        this.showToast('error', 'Email failed', errorMessage);
        this.loading.set(false);
        this.emailSending.set(false);
      }
    });
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
      manifest: this.http.get<EmailTemplateMetadata[]>(EMAIL_TEMPLATE_MANIFEST_URL),
      header: this.http.get('assets/email-templates/shared/email-header.html', { responseType: 'text' }),
      signature: this.http.get('assets/email-templates/shared/email-signature.html', { responseType: 'text' }),
      footer: this.http.get('assets/email-templates/shared/email-footer.html', { responseType: 'text' }),
      layout: this.http.get('assets/email-templates/shared/email-layout.html', { responseType: 'text' })
    }).subscribe({
      next: result => {
        const templates = result.manifest.filter(template => this.isValidTemplateMetadata(template));
        this.sharedEmailTemplateParts = {
          header: result.header,
          signature: result.signature,
          footer: result.footer,
          layout: result.layout
        };
        this.emailTemplates.set(templates);
        if (templates.length === 0) {
          this.showToast('error', 'Templates unavailable', 'No valid email templates were found.');
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
      error: () => this.showToast('error', 'Templates unavailable', 'Email template assets could not be loaded. Restart the UI server so Angular serves the new assets folder.')
    });
  }

  private applyEmailTemplate(templateId: string, confirmReplace: boolean): void {
    const template = this.emailTemplates().find(item => item.id === templateId) ?? this.selectedOrDefaultTemplate();
    if (!template || !this.isValidTemplateMetadata(template)) {
      this.showToast('error', 'Template unavailable', 'The selected email template is not valid.');
      return;
    }

    if (confirmReplace && this.draftDirty()) {
      const shouldReplace = window.confirm('Replace the current draft with this template?');
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
      error: () => this.showToast('error', 'Template unavailable', 'The current draft was preserved because the template file could not be loaded.')
    });
  }

  private applyLoadedTemplate(template: EmailTemplateMetadata, html: string): void {
    this.selectedEmailTemplateId.set(template.id);
    this.selectedEmailTemplateName.set(template.name);
    this.selectedEmailTemplateVersion.set(template.version);
    this.emailSubject.set(template.defaultSubject);
    this.emailBodyHtml.set(this.composeEmailTemplateHtml(html));
    this.emailPreview.set(null);
    this.draftDirty.set(false);
    this.emailEditorVersion++;
  }

  private isValidTemplateMetadata(template: EmailTemplateMetadata): boolean {
    return /^[a-z0-9-]+$/.test(template.id) &&
      template.htmlPath === `assets/email-templates/${template.id}/template.html` &&
      Boolean(template.name?.trim()) &&
      Boolean(template.version?.trim()) &&
      Array.isArray(template.supportedVariables) &&
      Array.isArray(template.requiredVariables);
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
      return 'No lead selected';
    }

    return `${lead.displayName}${lead.company?.name ? ` at ${lead.company.name}` : ''} <${lead.publicEmail || 'no email'}>`;
  }

  private leadSummary(lead: Lead | null): string {
    if (!lead) {
      return 'Selected lead';
    }

    return `${lead.displayName}${lead.company?.name ? ` at ${lead.company.name}` : ''}`;
  }

  protected canSendSelectedLead(): boolean {
    const lead = this.selectedLead();
    return Boolean(
      lead &&
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

  private buildEmailPreview(isTest: boolean, showFeedback = true): EmailPreview | null {
    const lead = this.selectedLead();
    if (!lead) {
      if (showFeedback) this.showToast('error', 'Email not ready', 'Select a lead before sending.');
      return null;
    }

    if (this.activeEmailLeadId() !== lead.id) {
      if (showFeedback) this.showToast('error', 'Email not ready', 'The draft belongs to another lead. Refresh the selected contact.');
      return null;
    }

    const leadEmail = lead.publicEmail?.trim() ?? '';
    const requestedRecipient = isTest ? SENDER_EMAIL : this.emailTo().trim();
    if (!this.isValidEmail(requestedRecipient)) {
      if (showFeedback) this.showToast('error', 'Email not ready', 'Enter a valid recipient email address.');
      return null;
    }

    if (!isTest) {
      if (!leadEmail) {
        if (showFeedback) this.showToast('error', 'Email not ready', 'This lead does not have a saved email address.');
        return null;
      }

      if (requestedRecipient.toLowerCase() !== leadEmail.toLowerCase()) {
        if (showFeedback) this.showToast('error', 'Email not ready', 'Recipient email must match the selected lead.');
        return null;
      }
    }

    const subject = this.resolveVariables(this.emailSubject(), lead, false).trim();
    const bodyHtml = this.finalEmailBodyHtml();
    const bodyText = this.htmlToText(bodyHtml);
    const unresolvedVariables = this.findUnresolvedVariables(`${subject}\n${bodyText}`);
    const missingRequiredVariables = this.getMissingRequiredVariables(lead);
    if (!subject || !bodyText) {
      if (showFeedback) this.showToast('error', 'Email not ready', 'Subject and message are required.');
      return null;
    }

    if (unresolvedVariables.length > 0 || missingRequiredVariables.length > 0) {
      if (showFeedback) this.showToast('error', 'Email not ready', 'Required template variables are missing or unresolved.');
      return null;
    }

    return {
      recipient: requestedRecipient,
      sender: `${SENDER_NAME} <${SENDER_EMAIL}>`,
      subject,
      bodyHtml,
      bodyText,
      warnings: this.getDraftWarnings(lead),
      isTest,
      templateName: this.selectedEmailTemplateName() || 'Custom draft',
      templateVersion: this.selectedEmailTemplateVersion() || 'draft'
    };
  }

  private defaultEmailCopy(): string {
    return [
      'Hi {{firstName}},',
      '',
      'I came across your work at {{company}} and thought Zextri could be useful for your growth workflow.',
      '',
      'Zextri helps teams identify relationships that need attention and write timely, context-aware follow-ups across LinkedIn and X.',
      '',
      'Would you be open to a quick look? I\'d be happy to send a short demo.'
    ].join('\n');
  }

  private resolveVariables(value: string, lead: Lead, forHtml = true): string {
    const company = lead.company?.name?.trim() || '';
    const variables: Record<string, string> = {
      firstName: this.firstName(lead),
      fullName: lead.displayName.trim(),
      company,
      senderName: 'Bharat Bhushan',
      senderTitle: 'Founder, Zextri',
      websiteUrl: WEBSITE_URL,
      demoUrl: DEMO_URL,
      unsubscribeUrl: UNSUBSCRIBE_URL,
      businessInfo: BUSINESS_INFO
    };

    return value.replace(/\{\{\s*([a-zA-Z0-9]+)\s*\}\}/g, (_, key: string) => {
      const replacement = variables[key] ?? '';
      return forHtml ? this.escapeHtml(replacement) : replacement;
    });
  }

  private getDraftWarnings(lead: Lead | null): string[] {
    if (!lead) {
      return ['No lead is selected.'];
    }

    const warnings: string[] = [];
    const firstName = this.firstName(lead);
    const company = lead.company?.name?.trim();
    const bodyText = this.normalizeText(this.htmlToText(this.finalEmailBodyHtml()));
    const subjectText = this.normalizeText(this.resolveVariables(this.emailSubject(), lead, false));
    const missingRequiredVariables = this.getMissingRequiredVariables(lead);
    const unresolvedVariables = this.findUnresolvedVariables(`${subjectText} ${bodyText}`);

    if (!firstName) warnings.push('Greeting may be missing because the lead name is blank.');
    if (!company) warnings.push('Company name is missing for this lead.');
    if (!lead.publicEmail) warnings.push('Recipient email is missing for this lead.');
    for (const variable of missingRequiredVariables) {
      warnings.push(`Required template variable is missing: {{${variable}}}.`);
    }
    if (this.emailTo().trim() && lead.publicEmail && this.emailTo().trim().toLowerCase() !== lead.publicEmail.trim().toLowerCase()) {
      warnings.push('Recipient email does not match the selected lead.');
    }
    if (unresolvedVariables.length > 0) {
      warnings.push(`Unresolved variables remain: ${unresolvedVariables.map(variable => `{{${variable}}}`).join(', ')}.`);
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
      company: lead.company?.name?.trim() || '',
      senderName: 'Bharat Bhushan',
      senderTitle: 'Founder, Zextri',
      websiteUrl: WEBSITE_URL,
      demoUrl: DEMO_URL,
      unsubscribeUrl: UNSUBSCRIBE_URL,
      businessInfo: BUSINESS_INFO
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
      this.emailSignatureHtml.set(this.zextriSignatureHtml());
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
      this.emailReplyTo.set(SENDER_EMAIL);
      this.emailCc.set(settings.cc ?? '');
      this.emailBcc.set(settings.bcc ?? '');
      this.emailSignatureHtml.set(this.normalizeSavedSignature(settings.signatureHtml, settings.signature));
      this.includeSignature.set(settings.includeSignature ?? true);
    } catch {
      this.message.set('Saved email settings could not be loaded.');
      this.emailSignatureHtml.set(this.zextriSignatureHtml());
    }
  }

  private finalEmailBodyHtml(): string {
    const lead = this.selectedLead();
    const sanitizedBody = this.sanitizeEditableHtml(this.emailBodyHtml());
    const resolvedBody = lead
      ? this.resolveVariables(sanitizedBody, lead)
      : sanitizedBody;

    if (this.selectedEmailTemplateId()) {
      return resolvedBody;
    }

    const signedBody = this.includeSignature()
      ? this.withDefaultSignature(resolvedBody)
      : this.removeSignature(resolvedBody);
    return this.withComplianceFooter(signedBody);
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
      `<div>${BUSINESS_INFO}</div>`,
      `<div><a href="${UNSUBSCRIBE_URL}" style="color:#0f766e;text-decoration:none;">Unsubscribe</a> from future outreach.</div>`,
      '</div>'
    ].join('');
    const withoutFooter = bodyHtml.replace(/<div data-signalminer-compliance="true"[\s\S]*?<\/div>\s*<\/div>/gi, '').trim();
    return `${withoutFooter}${footer}`;
  }

  private editableEmailBodyHtml(): string {
    return this.includeSignature()
      ? this.withDefaultSignature(this.emailBodyHtml())
      : this.removeSignature(this.emailBodyHtml());
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

    if (!legacySignature?.trim() ||
        legacySignature === LEGACY_EMAIL_SIGNATURE ||
        legacySignature === ZEXTRI_EMAIL_SIGNATURE_TEXT) {
      return this.zextriSignatureHtml();
    }

    return this.customSignatureHtml(legacySignature);
  }

  private isLegacyZextriSignature(signatureHtml: string): boolean {
    const normalizedText = this.normalizeText(this.htmlToText(signatureHtml));
    return normalizedText.includes('ai-assisted operations for faster-moving teams') ||
      normalizedText.includes('ai-powered social intelligence for linkedin and x') ||
      normalizedText.includes('write smarter. follow up better.');
  }

  private zextriSignatureHtml(): string {
    return [
      '<table data-signalminer-signature="true" role="presentation" cellpadding="0" cellspacing="0" style="margin-top:22px;border-collapse:collapse;font-family:Arial,Helvetica,sans-serif;color:#111827;">',
      '<tr><td colspan="2" style="padding:0 0 14px;"><div style="width:72px;height:2px;background:#0f766e;line-height:2px;font-size:2px;">&nbsp;</div></td></tr>',
      '<tr>',
      '<td style="width:56px;vertical-align:top;padding:0 14px 0 0;">',
      `<img src="${ZEXTRI_LOGO_URL}" alt="Zextri" width="44" height="44" style="display:block;width:44px;height:44px;border-radius:10px;border:1px solid #dbe3ea;">`,
      '</td>',
      '<td style="vertical-align:top;padding:0 0 0 14px;border-left:1px solid #d8dee6;">',
      '<div style="font-size:14px;line-height:20px;color:#374151;margin:0 0 8px;">Warm regards,</div>',
      '<div style="font-size:15px;line-height:21px;font-weight:700;color:#111827;margin:0;">Bharat Bhushan</div>',
      '<div style="font-size:13px;line-height:19px;color:#4b5563;margin:2px 0 8px;">Founder, Zextri</div>',
      '<div style="font-size:13px;line-height:19px;color:#374151;margin:0;">',
      '<a href="https://zextri.com" style="color:#0f766e;text-decoration:none;font-weight:700;">zextri.com</a>',
      '</div>',
      '</td>',
      '</tr>',
      '</table>'
    ].join('');
  }

  private customSignatureHtml(signature: string): string {
    return [
      '<div data-signalminer-signature="true" style="margin-top:18px;padding:12px 0 0;border-top:1px solid #e2e8f0;font-family:Inter,Segoe UI,Arial,sans-serif;color:#172033;">',
      this.textToHtml(signature),
      '</div>'
    ].join('');
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

    this.loading.set(true);
    const form = new FormData();
    form.append('file', file);
    form.append('commit', String(commit));

    this.http.post<LeadImportResult>(`${this.apiBase}/leads/import`, form).subscribe({
      next: result => {
        this.importResult.set(result);
        this.message.set(commit
          ? `Imported ${result.importedRows} leads; skipped ${result.skippedRows}.`
          : `Previewed ${result.validRows} valid rows from ${result.totalRows}.`);
        this.loading.set(false);
        if (commit) {
          this.sourceFilter.set('Imported');
          this.search();
        }
      },
      error: (error: HttpErrorResponse) => {
        this.message.set(error.error?.message || 'Import failed. Check the file format and required columns.');
        this.loading.set(false);
      }
    });
  }
}
