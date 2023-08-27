﻿using ChessChallenge.API;
using System;
using System.Collections.Generic;
using System.Linq;

public class MyBot : IChessBot
{
    private const PieceType PAWN = PieceType.Pawn;
    private const PieceType KNIGHT = PieceType.Knight;
    private const PieceType BISHOP = PieceType.Bishop;
    private const PieceType ROOK = PieceType.Rook;
    private const PieceType QUEEN = PieceType.Queen;

    private Board board;
    private Move bestMove;
    private int bestScore;
    private int startDepth;
    private int alphabetaNodes; // #DEBUG
    private int quiescenceNodes; // #DEBUG
    private int[,,] historyHeuristic;

    public Move Think(Board board, Timer timer)
    {
        this.board = board;

        for (startDepth = 1; startDepth <= 4; startDepth++)
        {
            bestScore = -999999;
            alphabetaNodes = 0; // #DEBUG
            quiescenceNodes = 0; // #DEBUG
            historyHeuristic = new int[2, 64, 64];

            int score = AlphaBetaSearch(startDepth, -999999, 999999);

            Console.WriteLine("Depth {2}: {0} (score = {1})", bestMove, score, startDepth); // #DEBUG
            Console.WriteLine("Nodes:        {0}", alphabetaNodes + quiescenceNodes); // #DEBUG
            Console.WriteLine("  AlphaBeta:  {0}", alphabetaNodes); // #DEBUG
            Console.WriteLine("  Quiescence: {0}", quiescenceNodes); // #DEBUG
            Console.WriteLine(); // #DEBUG
        }

        return bestMove;
    }

    private int AlphaBetaSearch(int depth, int alpha, int beta)
    {
        if (depth == 0)
        {
            return QuiescenceSearch(alpha, beta);
        }

        alphabetaNodes++; // #DEBUG

        if (board.IsInCheckmate())
        {
            return startDepth - depth - 100000;
        }

        if (board.IsDraw())
        {
            return 0;
        }

        foreach (Move move in OrderMoves(board.GetLegalMoves()))
        {
            board.MakeMove(move);
            int score = -AlphaBetaSearch(depth - 1, -beta, -alpha);
            int castlingIncentives = depth == startDepth ? TurochampCastlingIncentives(move) : 0;
            board.UndoMove(move);

            if (score > alpha)
            {
                alpha = score;

                if (score >= beta)
                {
                    historyHeuristic[IsWhiteToMove ? 0 : 1, move.StartSquare.Index, move.TargetSquare.Index] += depth * depth;
                    return beta;
                }
                
                score += castlingIncentives;
                if (depth == startDepth && score > bestScore)
                {
                    bestScore = score;
                    bestMove = move;
                }
            }
        }
        return alpha;
    }

    private int QuiescenceSearch(int alpha, int beta)
    {
        quiescenceNodes++; // #DEBUG
        int standScore = TurochampEvaluate();

        if (standScore >= beta)
        {
            return beta;
        }

        if (standScore > alpha)
        {
            alpha = standScore;
        }

        foreach (Move move in OrderMoves(board.GetLegalMoves(true)))
        {
            board.MakeMove(move);
            int score = -QuiescenceSearch(-beta, -alpha);
            board.UndoMove(move);

            if (score >= beta)
            {
                return beta;
            }

            if (score > alpha)
            {
                alpha = score;
            }
        }
        return alpha;
    }


    private int TurochampEvaluate()
    {
        var MaterialScoreForColor = (bool whiteColor) =>
        {
            var MaterialScoreForPiece = (PieceType pieceType) => board.GetPieceList(pieceType, whiteColor).Count * TurochampPieceMaterialValue(pieceType);
            return MaterialScoreForPiece(PAWN)
                + MaterialScoreForPiece(KNIGHT)
                + MaterialScoreForPiece(BISHOP)
                + MaterialScoreForPiece(ROOK)
                + MaterialScoreForPiece(QUEEN);
        };
        var PositionalScoreForCurrentPlayer = () =>
        {
            int positionalScore = 0;
            var nonPawnDefenders = NumberOfNonPawnDefenders();
            var pawnDefenders = NumberOfPawnDefenders();

            // Mobility score (rules 1, 3): use the fact that moves are grouped by piece
            int currentPieceIndex = -1;
            int currentMoveCount = 0;
            var FlushMobilityScore = () => (int)Math.Sqrt(10000 * currentMoveCount); // 100 * sqrt(numMoves)
            foreach (Move move in board.GetLegalMoves())
            {
                if (move.MovePieceType == PAWN || move.IsCastles)
                {
                    continue;
                }
                int fromIndex = move.StartSquare.Index;
                if (fromIndex != currentPieceIndex && currentPieceIndex != -1)
                {
                    positionalScore += FlushMobilityScore();
                    currentMoveCount = 0;
                }
                currentMoveCount += move.IsCapture ? 2 : 1;
                currentPieceIndex = fromIndex;
            }
            positionalScore += FlushMobilityScore();

            // Piece safety (rule 2)
            var AddPieceSafetyScoreNonPawn = (PieceType pieceType) =>
                ForEachPieceOfPlayerToMove(pieceType, piece =>
                {
                    int index = piece.Square.Index;
                    int defenders = nonPawnDefenders[index] + pawnDefenders[index];
                    positionalScore += defenders > 1 ? 150 : defenders > 0 ? 100 : 0; // 1 point if defended, 1.5 points if defended 2+ times
                });
            AddPieceSafetyScoreNonPawn(ROOK);
            AddPieceSafetyScoreNonPawn(BISHOP);
            AddPieceSafetyScoreNonPawn(KNIGHT);

            // King safety (rule 4)
            currentMoveCount = BitboardHelper.GetNumberOfSetBits(BitboardHelper.GetSliderAttacks(QUEEN, board.GetKingSquare(IsWhiteToMove), board));
            positionalScore -= FlushMobilityScore();


            // Pawn credit (rule 6)
            ForEachPieceOfPlayerToMove(PAWN, piece =>
            {
                Square square = piece.Square;
                positionalScore += (IsWhiteToMove ? square.Rank - 1 : 6 - square.Rank) * 20; // 0.2 points for each rank advanced
                positionalScore += nonPawnDefenders[square.Index] > 0 ? 30 : 0; // 0.3 points if defended by a non-pawn
            });

            // Mates and checks (rule 7) is not implemented (see README.md)

            return positionalScore;
        };
        int scoreCp = MaterialScoreForColor(IsWhiteToMove) - MaterialScoreForColor(!IsWhiteToMove) + PositionalScoreForCurrentPlayer();
        board.ForceSkipTurn();
        scoreCp -= PositionalScoreForCurrentPlayer();
        board.UndoSkipTurn();
        return scoreCp;
    }

    private int TurochampCastlingIncentives(Move move)
    {
        // Castling (rule 5)
        if (move.IsCastles)
        {
            // Existing implementations do stack the modifiers. See README.md
            return 300;
        }

        // We don't need to play the move, this function is called from AlphaBetaSearch when the move has already been played

        bool playerOfMove = !IsWhiteToMove; // Currently it's the opponent's turn
        if (!board.HasKingsideCastleRight(playerOfMove) && !board.HasKingsideCastleRight(playerOfMove))
        {
            // Since IsCastles = false, this move loses castling rights (and it must have been a king or rook move).
            // If we had already lost castling rights, this function always returns 0 for all moves, so no move has priority.
            return 0;
        }

        // We can castle. See if we can castle in the next turn
        board.ForceSkipTurn();
        foreach (Move nextMove in board.GetLegalMoves())
        {
            if (nextMove.IsCastles)
            {
                board.UndoSkipTurn();
                return 200;
            }
        }
        // We can castle, but not in the next turn.
        board.UndoSkipTurn();
        return 100;
    }

    private int TurochampPieceMaterialValue(PieceType pieceType) => pieceType switch
    {
        // Original Turochamp material values:
        PAWN => 100,
        KNIGHT => 300,
        BISHOP => 350,
        ROOK => 500,
        QUEEN => 1000,

        // Adjusted material values:
        //PAWN => 200,
        //KNIGHT => 600,
        //BISHOP => 700,
        //ROOK => 1000,
        //QUEEN => 2000,
        _ => 0,
    };

    private int[] NumberOfNonPawnDefenders()
    {
        var defenders = new int[64];
        var AddDefendersForPiece = (PieceType pieceType) =>
            ForEachPieceOfPlayerToMove(pieceType, piece =>
            {
                ulong bitboard = BitboardHelper.GetPieceAttacks(pieceType, piece.Square, board, true /* not used */);
                while (bitboard != 0)
                {
                    int index = BitboardHelper.ClearAndGetIndexOfLSB(ref bitboard);
                    defenders[index]++;
                }
            });
        AddDefendersForPiece(KNIGHT);
        AddDefendersForPiece(BISHOP);
        AddDefendersForPiece(ROOK);
        AddDefendersForPiece(QUEEN);
        return defenders;
    }

    private int[] NumberOfPawnDefenders()
    {
        var defenders = new int[64];
        ForEachPieceOfPlayerToMove(PAWN, pawn =>
        {
            ulong bitboard = BitboardHelper.GetPawnAttacks(pawn.Square, IsWhiteToMove);
            while (bitboard != 0)
            {
                int index = BitboardHelper.ClearAndGetIndexOfLSB(ref bitboard);
                defenders[index]++;
            }
        });
        return defenders;
    }

    private void ForEachPieceOfPlayerToMove(PieceType pieceType, Action<Piece> callback)
    {
        foreach (Piece piece in board.GetPieceList(pieceType, IsWhiteToMove))
        {
            callback(piece);
        }
    }

    private bool IsWhiteToMove => board.IsWhiteToMove;

    private IEnumerable<Move> OrderMoves(Move[] moves) => 
        moves.Select(move =>
        {
            int score = 0;
            if (move.IsCapture)
            {
                score += 100000 + TurochampPieceMaterialValue(move.CapturePieceType) - TurochampPieceMaterialValue(move.MovePieceType);
            }
            if (board.SquareIsAttackedByOpponent(move.TargetSquare))
            {
                score -= 50;
            }
            score += historyHeuristic[IsWhiteToMove ? 0 : 1, move.StartSquare.Index, move.TargetSquare.Index];

            return (move, score);
        }
        ).OrderByDescending(x => x.score).Select(x => x.move);
}

