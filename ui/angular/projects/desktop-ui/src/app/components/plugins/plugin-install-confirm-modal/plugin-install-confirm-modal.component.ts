import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, ViewChild, computed, inject, signal } from '@angular/core';
import { DomSanitizer, SafeUrl } from '@angular/platform-browser';
import { AppStrings, PLUGIN_WARNING_ARTIFACT_UNSIGNED, PLUGIN_WARNING_SIGNATURE_UNVERIFIED, PluginInstallActionResponse, PluginInstallWarning, PluginSignatureVerification } from '@macro-deck/runtime';
import { ButtonComponent, LocalizationService, ModalComponent, TranslatePipe, dismissModal } from '@shared';
import { LoadingStateComponent } from '../../feedback/loading-state/loading-state.component';

const TRUST_REFUSALS: Record<string, string> = {
  signature_invalid: AppStrings.Plugins.TrustRefusal.SignatureInvalid,
  content_mismatch: AppStrings.Plugins.TrustRefusal.ContentMismatch,
  malformed: AppStrings.Plugins.TrustRefusal.Malformed,
  untrusted_root: AppStrings.Plugins.TrustRefusal.UntrustedRoot,
  wrong_certificate_purpose: AppStrings.Plugins.TrustRefusal.WrongCertificatePurpose,
  certificate_not_valid_at_signature: AppStrings.Plugins.TrustRefusal.CertificateNotValidAtSignature,
  revoked: AppStrings.Plugins.TrustRefusal.Revoked,
  verification_unavailable: AppStrings.Plugins.TrustRefusal.VerificationUnavailable
};

interface WarningEntry {
  message: string;
  severity: 'warn' | 'error';
  blocking: boolean;
}

@Component({
  selector: 'shared-plugin-install-confirm-modal',
  standalone: true,
  imports: [ModalComponent, ButtonComponent, LoadingStateComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- Wider than a plain confirmation: the trust warning and the installer's own warnings are
         several lines of prose each, and at 450px they stack into a narrow column. -->
    <shared-modal
      [heading]="'macrodeck.app:Plugins.InstallConfirmHeading' | translate"
      size="medium"
      [closeOnOverlay]="!busy"
      (close)="onCancel()">
      <div class="plugin-install">
        @if (loading) {
          <shared-loading-state [message]="'macrodeck.app:Plugins.ReadingPackage' | translate" />
        } @else if (inspection(); as result) {
          <div class="plugin-install-head">
            <div class="plugin-install-icon">
              @if (iconUrl(); as icon) {
                <img [src]="icon" alt="">
              } @else {
                <span class="icon icon-puzzle icon-2xl"></span>
              }
            </div>
            <dl class="plugin-install-facts">
              <dt>{{ 'macrodeck.app:Plugins.FieldName' | translate }}</dt>
              <dd>{{ result.artifact?.name ?? result.pluginId }}</dd>

              <dt>{{ 'macrodeck.app:Plugins.FieldPackageId' | translate }}</dt>
              <dd class="plugin-install-mono">{{ result.pluginId }}</dd>

              <dt>{{ 'macrodeck.app:Plugins.FieldSigningStatus' | translate }}</dt>
              <dd [class]="signingTone()" [title]="signingDetail()">
                @if (isVerified()) {
                  <span class="icon icon-check icon-xs" data-testid="plugin-signature-verified"></span>
                }
                {{ signingLabel() }}
              </dd>

              <dt>{{ 'macrodeck.app:Plugins.FieldAuthor' | translate }}</dt>
              <dd [class]="authorTone()">
                {{ publisherName() }}
                @if (hasPublisher() && !isVerified()) {
                  <!-- Only meaningful next to a name: with no author there is no claim to qualify. -->
                  <span class="plugin-install-claim">{{ 'macrodeck.app:Plugins.UnverifiedClaim' | translate }}</span>
                }
              </dd>

              <dt>{{ 'macrodeck.app:Plugins.FieldVersion' | translate }}</dt>
              <dd>{{ result.version }}</dd>

              @if (isSupported() !== null) {
                <dt>{{ 'macrodeck.app:Plugins.FieldSupportedOnPlatform' | translate }}</dt>
                <dd [class.is-danger]="isSupported() === false">
                  {{ (isSupported() ? 'macrodeck:Common.Yes' : 'macrodeck:Common.No') | translate }}
                </dd>
              }

              @if (result.artifact?.description; as description) {
                <dt>{{ 'macrodeck.app:Plugins.FieldDescription' | translate }}</dt>
                <dd>{{ description }}</dd>
              }
            </dl>
          </div>

          @if (isVerified()) {
            <p class="plugin-install-hint">
              @if (hasPublisher()) {
                {{ 'macrodeck.app:Plugins.VerifiedWithPublisherHint' | translate }}
              } @else {
                {{ 'macrodeck.app:Plugins.VerifiedNoPublisherHint' | translate }}
              }
            </p>
          } @else {
            <div class="plugin-install-warning">
              <span class="icon icon-alert-triangle icon-sm"></span>
              <p>
                {{ signatureLead() }} {{ 'macrodeck.app:Plugins.UntrustedInstallWarning' | translate }}
              </p>
            </div>
          }

          @if (blockedReason(); as reason) {
            <p class="plugin-install-error">
              <span class="icon icon-alert-triangle icon-sm"></span>
              <span>{{ reason }}</span>
            </p>
          }

          @if (warnings().length > 0) {
            <div class="plugin-install-section">
              <p class="plugin-install-title">{{ 'macrodeck.app:Plugins.BeforeYouInstallTitle' | translate }}</p>
              <ul class="plugin-install-warnings">
                @for (warning of warnings(); track warning.message) {
                  <li [class]="'is-' + warning.severity">
                    <span>{{ warning.message }}</span>
                    @if (warning.blocking) {
                      <span class="plugin-install-blocking">
                        {{ 'macrodeck.app:Plugins.BlockingWarningNote' | translate }}
                      </span>
                    }
                  </li>
                }
              </ul>
            </div>
          }

          @if (isUnsigned() && unsignedChoiceOffered()) {
            <label class="plugin-install-consent">
              <input
                type="checkbox"
                [checked]="unsignedAccepted()"
                [disabled]="busy"
                (change)="onUnsignedAcceptedChange($event)" />
              <span>
                {{ 'macrodeck.app:Plugins.UnsignedConsentText' | translate }}
              </span>
            </label>
          }

          @if (isVerified()) {
            <!-- The unverified branch says this at length; a reviewed plugin still runs as a program. -->
            <p class="plugin-install-trust">
              {{ 'macrodeck.app:Plugins.RunsAsProgramNote' | translate }}
            </p>
          }
        }
      </div>
      <div modal-footer>
        <shared-button variant="secondary" [disabled]="busy" (click)="onCancel()">{{ 'macrodeck:Common.Cancel' | translate }}</shared-button>
        <shared-button
          variant="primary"
          [loading]="busy"
          [disabled]="loading || blockedReason() !== null || !consentSatisfied() || busy"
          (click)="onConfirm()">
          {{ 'macrodeck.app:Plugins.InstallButton' | translate }}
        </shared-button>
      </div>
    </shared-modal>
  `,
  styleUrls: ['./plugin-install-confirm-modal.component.scss']
})
export class PluginInstallConfirmModalComponent {
  @Input()
  set inspectionResult(value: PluginInstallActionResponse | null) {
    this.inspection.set(value);
  }

  @Input() busy = false;

  @Input() loading = false;

  @Input()
  set allowUnsignedChoice(value: boolean) {
    this.unsignedChoiceOffered.set(value);
  }

  @Output() confirmed = new EventEmitter<{ allowUnsigned: boolean }>();
  @Output() cancelled = new EventEmitter<void>();

  @ViewChild(ModalComponent) private modal?: ModalComponent;

  private readonly sanitizer = inject(DomSanitizer);
  private readonly localization = inject(LocalizationService);

  protected readonly inspection = signal<PluginInstallActionResponse | null>(null);

  protected readonly verdict = computed<PluginSignatureVerification>(
    () => this.inspection()?.signature?.verification ?? 'unverified'
  );

  protected readonly isVerified = computed(
    () => this.verdict() === 'valid' && this.inspection()?.signature?.trusted !== false
  );

  protected readonly isRejected = computed(() => this.verdict() === 'invalid');

  protected readonly isUnsigned = computed(() => this.verdict() === 'not_signed');

  protected readonly unsignedChoiceOffered = signal(false);

  protected readonly unsignedAccepted = signal(false);

  protected readonly consentSatisfied = computed(
    () => !this.isUnsigned() || !this.unsignedChoiceOffered() || this.unsignedAccepted()
  );

  protected readonly authorTone = computed(() => {
    if (this.isVerified()) {
      return 'is-success';
    }
    return this.hasPublisher() ? 'is-warning' : 'is-danger';
  });

  protected readonly blockedReason = computed<string | null>(() => {
    if (this.isRejected() || this.verdict() === 'unverified') {
      const key = TRUST_REFUSALS[this.inspection()?.signature?.category ?? ''] ??
        AppStrings.Plugins.UntrustedBlockedReason;
      return this.localization.translateKey(key);
    }
    if (this.isSupported() === false) {
      return this.localization.translateKey(AppStrings.Plugins.UnsupportedPlatformReason);
    }
    return null;
  });

  protected readonly publisherName = computed(
    () => this.inspection()?.publisher?.name ?? this.localization.translateKey(AppStrings.Plugins.UnknownPublisher)
  );

  protected readonly hasPublisher = computed(() => !!this.inspection()?.publisher?.name);

  protected readonly isSupported = computed(
    () => this.inspection()?.artifact?.supportedOnThisPlatform ?? null
  );

  // The icon lives inside the artifact, so it can only arrive inline. Angular's URL sanitizer allows
  // data: images but not image/svg+xml, which is what plugins actually ship, so the value is
  // checked against the exact shapes the host emits and then trusted. Safe because it is bound to an
  // <img>: a browser refuses to run script in an SVG loaded that way.
  protected readonly iconUrl = computed<SafeUrl | null>(() => {
    const icon = this.inspection()?.artifact?.iconDataUri;
    if (!icon || !/^data:image\/(?:svg\+xml|png|jpeg|webp);base64,[a-z0-9+/]+=*$/i.test(icon)) {
      return null;
    }
    return this.sanitizer.bypassSecurityTrustUrl(icon);
  });

  protected readonly signatureLead = computed(() =>
    this.localization.translateKey(
      this.verdict() === 'not_signed'
        ? AppStrings.Plugins.SignatureLeadNotSigned
        : AppStrings.Plugins.SignatureLeadUnverified
    )
  );

  protected readonly signingLabel = computed(() => {
    switch (this.verdict()) {
      case 'valid':
        return this.localization.translateKey(AppStrings.Plugins.SigningLabelValid);
      case 'invalid':
        return this.localization.translateKey(AppStrings.Plugins.SigningLabelInvalid);
      case 'not_signed':
        return this.localization.translateKey(AppStrings.Plugins.SigningLabelNotSigned);
      default:
        return this.localization.translateKey(AppStrings.Plugins.SigningLabelUnverifiable);
    }
  });

  protected readonly signingTone = computed(() => {
    switch (this.verdict()) {
      case 'valid':
        return 'is-success';
      case 'invalid':
        return 'is-danger';
      default:
        return 'is-warning';
    }
  });

  protected readonly signingDetail = computed(() => {
    const signature = this.inspection()?.signature;
    const claim = signature?.keyId
      ? (signature.algorithm
        ? this.localization.translateKey(AppStrings.Plugins.ClaimsKeyWithAlgorithm, { keyId: signature.keyId, algorithm: signature.algorithm })
        : this.localization.translateKey(AppStrings.Plugins.ClaimsKey, { keyId: signature.keyId }))
      : null;
    return [signature?.message, claim].filter(Boolean).join(' ');
  });

  protected readonly warnings = computed<WarningEntry[]>(() =>
    (this.inspection()?.warnings ?? [])
      .filter(warning => !isSignatureWarning(warning))
      .map(warning => ({
        message: warning.message,
        severity: warning.severity === 'blocking' ? 'error' : 'warn',
        blocking: warning.severity === 'blocking'
      }))
  );

  protected onUnsignedAcceptedChange(event: Event): void {
    this.unsignedAccepted.set((event.target as HTMLInputElement).checked);
  }

  protected onConfirm(): void {
    this.confirmed.emit({ allowUnsigned: this.isUnsigned() && this.unsignedAccepted() });
  }

  protected onCancel(): void {
    if (this.busy) {
      return;
    }
    dismissModal(this.modal, () => this.cancelled.emit());
  }
}

function isSignatureWarning(warning: PluginInstallWarning): boolean {
  return warning.code === PLUGIN_WARNING_ARTIFACT_UNSIGNED
    || warning.code === PLUGIN_WARNING_SIGNATURE_UNVERIFIED;
}
