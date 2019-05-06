import { AxiosResponse, AxiosRequestConfig, AxiosError, AxiosInstance } from 'axios';

import { IAuthenticationInfo, IUserInfo, IAuthServiceConfiguration, IError, } from './index';
import { IAuthenticationInfoTypeSystem, StdAuthenticationTypeSystem, PopupDescriptor } from './index.extension';
import { IWebFrontAuthResponse, AuthServiceConfiguration } from './index.private';

export class AuthService<T extends IUserInfo = IUserInfo> {

    private _authenticationInfo: IAuthenticationInfo<T>;
    private _token: string;
    private _refreshable: boolean;
    private _availableSchemes: string[];
    private _retrievedError: IError;
    private _version: string;
    private _configuration: AuthServiceConfiguration;

    private _axiosInstance: AxiosInstance;
    private _typeSystem: IAuthenticationInfoTypeSystem<T>;
    private _popupDescriptor: PopupDescriptor;

    private _expTimer;
    private _cexpTimer;

    private _subscribers: Set<() => void>;

    public get authenticationInfo(): IAuthenticationInfo<T> { return this._authenticationInfo; }
    public get token(): string { return this._token; }
    public get refreshable(): boolean { return this._refreshable; }
    public get availableSchemes(): string[] { return this._availableSchemes; }
    public get version(): string { return this._version; }
    public get errorCollector(): IError { return this._retrievedError; }

    public get popupDescriptor(): PopupDescriptor {
        if (!this._popupDescriptor) { this._popupDescriptor = new PopupDescriptor(); }
        return this._popupDescriptor;
    }
    public set popupDescriptor(popupDescriptor: PopupDescriptor) {
        if (popupDescriptor) { this._popupDescriptor = popupDescriptor; }
    };

    private readonly _noRetrievedError: IError = {
        loginFailureCode: null,
        loginFailureReason: null,
        errorId: null,
        errorReason: null
    };

    //#region constructor

    constructor(
        configuration: IAuthServiceConfiguration,
        axiosInstance: AxiosInstance,
        typeSystem?: IAuthenticationInfoTypeSystem<T>
    ) {
        if (!configuration) { throw new Error('Confiugration must be defined.'); }
        this._configuration = new AuthServiceConfiguration(configuration);

        if (!axiosInstance) { throw new Error('AxiosInstance must be defined.'); }
        this._axiosInstance = axiosInstance;
        this._axiosInstance.interceptors.request.use(this.onIntercept());

        this._typeSystem = typeSystem ? typeSystem : new StdAuthenticationTypeSystem() as any;
        this._version = '';
        this._availableSchemes = [];
        this._subscribers = new Set<() => void>();
        this._expTimer = null;
        this._cexpTimer = null;

        if (!(typeof window === 'undefined')) {
            window.addEventListener('message', this.onMessage(), false);
        }

        this.handleResponseError();
    }

    public static async createAsync<T extends IUserInfo = IUserInfo>(
        configuration: IAuthServiceConfiguration,
        axiosInstance: AxiosInstance,
        typeSystem?: IAuthenticationInfoTypeSystem<T>
    ): Promise<AuthService> {
        const authService = new AuthService<T>(configuration, axiosInstance, typeSystem);
        try {
            await authService.refresh(true, true);
            return authService;
        } catch (error) {
            if (console.error) { console.error(error); }
            else { console.log(error); }
            return authService;
        }
    }

    //#endregion

    //#region events

    private onIntercept(): (value: AxiosRequestConfig) => AxiosRequestConfig | Promise<AxiosRequestConfig> {
        return (config: AxiosRequestConfig) => {
            if (config.url.startsWith(this._configuration.webFrontAuthEndPoint) && this._token) {
                Object.assign(config.headers, { Authorization: `Bearer ${this._token}` });
            }
            return config;
        };
    }

    private onMessage(): (this: Window, ev: MessageEvent) => void {
        return (messageEvent) => {
            if (messageEvent.data.WFA === 'WFA') {
                const origin = messageEvent.origin + '/';
                if (origin !== this._configuration.webFrontAuthEndPoint) {
                    throw new Error('Incorrect origin in postMessage.');
                }
                this.parseResonse(messageEvent.data.data);
            }
        };
    }

    //#endregion

    //#region request handling

    private async sendRequest(
        entryPoint: string,
        requestOptions?: { body?: object, queries?: Array<string | { key: string, value: string }> }
    ): Promise<void> {
        try {
            const query = requestOptions.queries && requestOptions.queries.length
                ? `?${requestOptions.queries.map(q => typeof q === 'string' ? q : `${q.key}=${q.value}`).join('&')}`
                : '';
            const response = await this._axiosInstance.post<IWebFrontAuthResponse>(
                `${this._configuration.webFrontAuthEndPoint}.webfront/c/${entryPoint}${query}`,
                requestOptions.body ? JSON.stringify(requestOptions.body) : {},
                { withCredentials: true });

            return response.status === 200
                ? this.parseResonse(response.data)
                : this.handleResponseError();
        } catch (error) {
            this.handleHttpErrorStatus((error as AxiosError).response);
        }
    }

    private parseResonse(response: IWebFrontAuthResponse): void {
        if (!(response)) {
            this.handleResponseError();
            return;
        }

        const loginFailureCode: number = response.loginFailureCode;
        const loginFailureReason: string = response.loginFailureReason;
        const errorId: string = response.errorId;
        const errorText: string = response.errorText;

        this._retrievedError = this._noRetrievedError;

        if (loginFailureCode && loginFailureReason) {
            this._retrievedError = {
                ...this._retrievedError,
                loginFailureCode: loginFailureCode,
                loginFailureReason: loginFailureReason
            };
        }

        if (errorId && errorText) {
            this._retrievedError = {
                ...this._retrievedError,
                errorId: errorId,
                errorReason: errorText
            };
        }

        if (this._retrievedError !== this._noRetrievedError) {
            this.handleResponseError();
            return;
        }

        if (response.version) { this._version = response.version; }
        if (response.schemes) { this._availableSchemes = response.schemes; }

        if (!response.info) {
            this.handleResponseError();
            return;
        }

        this._token = response.token ? response.token : '';
        this._refreshable = response.refreshable ? response.refreshable : false;
        this._authenticationInfo = this._typeSystem.authenticationInfo.fromJson(response.info);

        this.onChange();
    }

    private handleHttpErrorStatus(errorResponse: AxiosResponse): void {
        this.handleResponseError();
        this._retrievedError = {
            ...this._noRetrievedError,
            errorId: `HTTP status: ${errorResponse.status}`,
            errorReason: errorResponse.statusText
        }
    }

    private handleResponseError(): void {
        this._token = '';
        this._refreshable = false;
        this._authenticationInfo = this._typeSystem.authenticationInfo.none;
        if (this._expTimer !== null) {
            clearTimeout(this._expTimer);
            this._expTimer = null;
        }
        if (this._cexpTimer !== null) {
            clearTimeout(this._cexpTimer);
            this._cexpTimer = null;
        }
        this.onChange();
    }

    //#endregion

    //#region webfrontauth protocol

    public async basicLogin(userName: string, password: string): Promise<void> {
        await this.sendRequest('basicLogin', { body: { userName, password } });
    }

    public async unsafeDirectLogin(provider: string, payload: object): Promise<void> {
        await this.sendRequest('unsafeDirectLogin', { body: { provider, payload } });
    }

    public async refresh(full: boolean = false, requestSchemes: boolean = false, requestVersion: boolean = false): Promise<void> {
        const queries = [];
        if (full) { queries.push('full'); }
        if (requestSchemes) { queries.push('schemes'); }
        if (requestVersion) { queries.push('version'); }
        await this.sendRequest('refresh', { queries });
    }

    public async impersonate(user: string | number): Promise<void> {
        const requestOptions = { body: (typeof user === 'string') ? { userName: user } : { userId: user } };
        await this.sendRequest('impersonate', requestOptions);
    }

    public async logout(full: boolean = false): Promise<void> {
        this._token = '';
        await this.sendRequest('logout', { queries: full ? ['full'] : [] });
        await this.refresh();
    }

    public async startInlineLogin(scheme: string, returnUrl: string): Promise<void> {
        if (!returnUrl) { throw new Error('returnUrl must be defined.'); }
        if (!(returnUrl.startsWith('http://') || returnUrl.startsWith('https://'))) {
            if (returnUrl.charAt(0) !== '/') { returnUrl = '/' + returnUrl; }
            returnUrl = document.location.origin + returnUrl;
        }

        const queries = [{ key: 'scheme', value: scheme }, { key: 'returnUrl', value: encodeURI(returnUrl) }];
        await this.sendRequest('startLogin', { queries });
    }

    public async startPopupLogin(scheme: string, userData?: object): Promise<void> {

        if (scheme === 'Basic') {
            const popup = window.open('about:blank', this.popupDescriptor.popupTitle, this.popupDescriptor.features);
            popup.document.write(this.popupDescriptor.generateBasicHtml());

            const onClick = async () => {

                const usernameInput = popup.document.getElementById('username-input') as HTMLInputElement;
                const passwordInput = popup.document.getElementById('password-input') as HTMLInputElement;
                const errorDiv = popup.document.getElementById('error-div') as HTMLInputElement;
                const loginData = { username: usernameInput.value, password: passwordInput.value };

                if (!(loginData.username && loginData.password)) {
                    errorDiv.innerHTML = this.popupDescriptor.basicMissingCredentialsError;
                    errorDiv.style.display = 'block';
                } else {
                    await this.basicLogin(loginData.username, loginData.password);

                    if (this.authenticationInfo.level >= 2) {
                        popup.close();
                    } else {
                        errorDiv.innerHTML = this.popupDescriptor.basicInvalidCredentialsError;
                        errorDiv.style.display = 'block';
                    }
                }
            }

            popup.document.getElementById('submit-button').onclick = (async () => await onClick());
        } else {
            const url = `${this._configuration.webFrontAuthEndPoint}.webfront/c/startLogin`;
            userData = { ...userData, callerOrigin: document.location.origin };
            const queryString = Object.keys(userData).map((key) => encodeURIComponent(key) + '=' + encodeURIComponent(userData[key])).join('&');
            const finalUrl = url + '?scheme=' + scheme + ((queryString !== '') ? '&' + queryString : '');
            window.open(finalUrl, this.popupDescriptor.popupTitle, this.popupDescriptor.features);
        }
    }

    //#endregion

    //#region onChange

    private onChange(): void {
        this._subscribers.forEach(func => func());
    }

    public addOnChange(func: () => void): void {
        if (func !== undefined && func !== null) { this._subscribers.add(func); }
    }

    public removeOnChange(func: () => void): boolean {
        return this._subscribers.delete(func);
    }

    //#endregion
}
