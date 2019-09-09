import { IWebFrontAuthError, WellKnownError, IResponseError, ILoginError } from "./authService.model.public";

export type Collector = (s: string) => void;

export class WebFrontAuthError implements IWebFrontAuthError {
    public readonly type: string;
    public readonly errorId: string;
    public readonly errorReason: string;

    constructor(public readonly error: WellKnownError) {
        if (this.isErrorType<IResponseError>(error)) {
            this.type = "Protocol";
            this.errorId = error.errorId;
            this.errorReason = error.errorReason;
        } else if (this.isErrorType<ILoginError>(error)) {
            this.type = "Login";
            this.errorId = error.loginFailureCode.toString();
            this.errorReason = error.loginFailureReason;
        } else {
            throw new Error(`Invalid argument: error ${error}`);
        }
    }

    protected isErrorType<T extends WellKnownError>(error: WellKnownError): error is T {
        return !!(error as T);
    }

    public static NoError: IWebFrontAuthError = {
        type: 'No Error',
        error: null,
        errorId: '',
        errorReason: ''
    };
}

